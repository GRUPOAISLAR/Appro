#!/usr/bin/env bash
# Prueba de punta a punta sin cámaras:
#   simulador → Mosquitto → conector → ingesta (SQLite) → evaluador
# con un corte de la ingesta en el medio para verificar la cola y el reenvío.
#
# Requisitos: .NET 10 SDK, mosquitto 2.x, python3 y curl. Todo corre en
# 127.0.0.1, en una carpeta temporal, con patentes inventadas y una clave de hash
# generada en el momento. No toca ninguna base real.
#
# Uso: scripts/prueba-punta-a-punta.sh [autos por tanda, 100 por defecto]
set -euo pipefail

AUTOS="${1:-100}"
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d -t video-e2e-XXXXXX)"
P_MQTT=18831; P_INGESTA=18832; P_CONECTOR=18833
PIDS=()

limpiar() {
  for pid in "${PIDS[@]:-}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
}
trap limpiar EXIT

paso() { printf '\n== %s\n' "$*"; }
falla() { printf 'FALLA: %s\n' "$*" >&2; exit 1; }
esperar() { # esperar <segundos> <comando...>
  local hasta=$(( $(date +%s) + $1 )); shift
  until "$@" >/dev/null 2>&1; do
    [ "$(date +%s)" -ge "$hasta" ] && return 1
    sleep 0.5
  done
}
pendientes() { curl -sf "http://127.0.0.1:$P_CONECTOR/salud" | python3 -c 'import sys,json;print(json.load(sys.stdin)["colaPendiente"])'; }

paso "Compilando en $TMP"
for p in Ingesta Conector Herramientas; do
  dotnet build "$RAIZ/src/Bravo.Video.$p" -c Release -o "$TMP/$p" -v q -nologo >/dev/null
done

paso "Mosquitto en el puerto $P_MQTT"
printf 'listener %s 127.0.0.1\nallow_anonymous true\npersistence false\n' "$P_MQTT" > "$TMP/mosquitto.conf"
mosquitto -c "$TMP/mosquitto.conf" > "$TMP/mosquitto.log" 2>&1 & PIDS+=($!)

paso "Ingesta"
export VIDEO_DB_PROVEEDOR=Sqlite
export VIDEO_DB_CONEXION="Data Source=$TMP/video.db"
export VIDEO_CLAVE_HASH_PATENTE="$(head -c 32 /dev/urandom | base64)"
export VIDEO_RETENCION_PATENTE_DIAS=30
ingesta() { (cd "$TMP/Ingesta" && dotnet Bravo.Video.Ingesta.dll "$@"); }
ingesta migrar >/dev/null
TOKEN="$(ingesta equipo-alta --dispositivo minipc-01 --sede AR-PRUEBA-01 | tail -1)"
levantar_ingesta() {
  (cd "$TMP/Ingesta" && ASPNETCORE_URLS="http://127.0.0.1:$P_INGESTA" exec dotnet Bravo.Video.Ingesta.dll) >> "$TMP/ingesta.log" 2>&1 &
  PID_INGESTA=$!; PIDS+=($PID_INGESTA)
  esperar 30 curl -sf "http://127.0.0.1:$P_INGESTA/salud" || falla "la ingesta no levantó"
}
levantar_ingesta

paso "Conector"
cat > "$TMP/conector.json" <<EOF
{ "sede": "AR-PRUEBA-01", "dispositivo": "minipc-01", "pais": "AR",
  "zonaHoraria": "America/Argentina/Buenos_Aires",
  "urlIngesta": "http://127.0.0.1:$P_INGESTA/", "directorioCola": "$TMP/cola",
  "mqtt": { "host": "127.0.0.1", "puerto": $P_MQTT, "versionFrigate": "0.17.1" },
  "camaras": [ { "camara": "porton", "motor": "frigate", "rol": "PorZona", "zonaIngreso": "entrada", "zonaEgreso": "salida" } ] }
EOF
(cd "$TMP/Conector" && VIDEO_CONECTOR_CONFIG="$TMP/conector.json" VIDEO_CONECTOR_TOKEN="$TOKEN" \
  ASPNETCORE_URLS="http://127.0.0.1:$P_CONECTOR" exec dotnet Bravo.Video.Conector.dll) > "$TMP/conector.log" 2>&1 & PIDS+=($!)
esperar 30 sh -c "curl -sf http://127.0.0.1:$P_CONECTOR/salud | grep -q '\"mqttConectado\":true'" || falla "el conector no se conectó a MQTT"

simular() { (cd "$TMP/Herramientas" && dotnet bravo-video.dll simular --modo mqtt --mqtt-puerto "$P_MQTT" --autos "$AUTOS" "$@"); }

paso "Tanda 1 con todo andando"
simular --semilla 1 --inicio "$(date -d "-3 days" +%F)" --salida "$TMP/tanda1"
esperar 60 sh -c "[ \"\$(curl -sf http://127.0.0.1:$P_CONECTOR/salud | python3 -c 'import sys,json;print(json.load(sys.stdin)[\"colaPendiente\"])')\" = 0 ]" \
  || falla "la cola no se vació en la tanda 1"
echo "cola vacía"

paso "Corte: se apaga la ingesta y corre la tanda 2"
kill "$PID_INGESTA"; wait "$PID_INGESTA" 2>/dev/null || true
simular --semilla 2 --inicio "$(date -d "-2 days" +%F)" --salida "$TMP/tanda2"
sleep 3
EN_COLA="$(pendientes)"
echo "eventos en cola con la ingesta caída: $EN_COLA"
[ "$EN_COLA" -gt 0 ] || falla "con la ingesta caída la cola debería tener eventos"

paso "Vuelve la ingesta"
levantar_ingesta
esperar 360 sh -c "[ \"\$(curl -sf http://127.0.0.1:$P_CONECTOR/salud | python3 -c 'import sys,json;print(json.load(sys.stdin)[\"colaPendiente\"])')\" = 0 ]" \
  || falla "la cola no se vació después del corte"
echo "cola vacía"

paso "Verificación contra la verdad del simulador"
python3 - "$TMP" <<'PY'
import csv, sqlite3, sys
tmp = sys.argv[1]
esperados = set()
for tanda in ("tanda1", "tanda2"):
    with open(f"{tmp}/{tanda}/etiquetas.csv", newline="", encoding="utf-8") as f:
        esperados |= {r["eventId"] for r in csv.DictReader(f) if r["eventId"]}
db = sqlite3.connect(f"{tmp}/video.db")
filas = db.execute("select lower(EventId) from Eventos where Tipo like 'VEHICULO_%'").fetchall()
recibidos = [r[0] for r in filas]
print(f"esperados {len(esperados)}, recibidos {len(recibidos)}, distintos {len(set(recibidos))}")
assert len(recibidos) == len(set(recibidos)), "hay eventos duplicados"
faltan = esperados - set(recibidos); sobran = set(recibidos) - esperados
assert not faltan, f"faltan {len(faltan)} eventos"
assert not sobran, f"sobran {len(sobran)} eventos"
latidos = db.execute("select count(*) from Eventos where Tipo = 'DISPOSITIVO_LATIDO'").fetchone()[0]
print(f"latidos recibidos: {latidos}")

# Exportación de prueba para el evaluador (patentes inventadas de la simulación).
with open(f"{tmp}/tanda1/lecturas.csv", "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f, lineterminator="\n")
    w.writerow(["eventId","ts","tipo","camara","patenteLeida","patenteNormalizada","valida","confianza"])
    for r in db.execute("select lower(EventId), TsUtc, TsOffsetMinutos, Tipo, Camara, PatenteLeida, PatenteNormalizada, PatenteValida, PatenteConfianza from Eventos where Tipo like 'VEHICULO_%'"):
        ts = r[1].replace(" ", "T") + "+00:00"
        w.writerow([r[0], ts, r[3], r[4], r[5] or "", r[6] or "", "true" if r[7] else "false", r[8] if r[8] is not None else ""])
PY

paso "Logs sin patentes"
if grep -E -h 'Z[V-Z][0-9OIBSZG]{3}[A-Z0-9]{2}' "$TMP"/conector.log "$TMP"/ingesta.log; then
  falla "aparecen patentes en los logs"
fi
echo "ninguna patente en conector.log ni ingesta.log"

paso "Evaluación de la tanda 1"
(cd "$TMP/Herramientas" && dotnet bravo-video.dll evaluar --etiquetas "$TMP/tanda1/etiquetas.csv" --lecturas "$TMP/tanda1/lecturas.csv") \
  | sed -n '/^| Período/,/^$/p'

paso "OK — todo en $TMP"
