# Análisis de video — Plan de construcción por etapas

Proyecto: prueba técnica en Argentina (empresa con ingreso, egreso y estacionamiento) y después piloto en México.
Fecha: 4 de noviembre de 2026. Última actualización: 4 de noviembre de 2026 (segunda sesión).

Cada etapa dice qué construyo, qué la bloquea y qué queda listo al terminarla. Lo que tienen que preparar ustedes está en `preparativos.md`.

---

## Etapa 0 — Entorno · HECHA

- SDK de .NET 10 (10.0.114). En el entorno de Claude se instala con `apt-get install dotnet-sdk-10.0`: el instalador oficial (`builds.dotnet.microsoft.com`) está bloqueado por la red.
- Diseño y contrato de eventos v1 confirmados.
- ~~NuGet cerrado~~ **NuGet habilitado** (verificado el 4/11: restaura xUnit sin problema).
- **Ubicación provisoria del código:** `GRUPOAISLAR/Appro`, carpeta `video-analitica/`, rama `claude/new-session-149nqt`. Se mueve con su historial al repo definitivo cuando exista.
- El código de la primera sesión de la Etapa 1 se perdió porque no llegó a subirse a ningún repo. Se rehízo completo en la segunda.

## Etapa 1 — Núcleo sin dependencias externas · HECHA

Código en `src/`, pruebas en `tests/` (77 pruebas, todas con datos inventados).

- **Contratos (`Bravo.Video.Contratos`):** los eventos v1 en C# y su JSON Schema.
  - Sobre común: `eventId`, `schemaVersion`, `tipo`, `sede`, `dispositivo`, `camara`, `ts` con zona horaria, `fuente` (motor y versión).
  - Tipos: `VEHICULO_INGRESO`, `VEHICULO_EGRESO`, `PERSONAS_CRUCE`, `BOX_OCUPADO`, `BOX_LIBRE`, `DISPOSITIVO_LATIDO`.
- **Patentes (`Bravo.Video.Patentes`):** normalización y validación.
  - Argentina: Mercosur `AB123CD`, formato viejo `ABC123`, motos `A123BCD` y `123ABC`.
  - México: autos `ABC123`, `ABC1234`, `ABC123D`; camiones `AB1234C`; motos `Y123AB`.
  - Corrige caracteres parecidos según la posición (O/0, I/1, B/8, S/5, Z/2, G/6) y marca si el formato es válido.
- **Lectores de mensajes de cada motor:**
  - Frigate: `frigate/events`, `frigate/tracked_object_update` y `frigate/available`.
  - Cámara Hikvision ANPR: alerta XML `EventNotificationAlert`.
  - Sensor de conteo tipo Milesight: reporte `line_trigger_data`.
- **Estadías:** emparejar ingreso con egreso por patente y sede, con regla escrita y trazable (cada estadía guarda los dos `eventId` que la forman).

**Queda listo:** librerías compiladas y probadas con datos inventados.

**Pendiente de validar con equipos reales:** los formatos de Frigate, Hikvision y Milesight se tomaron de su documentación pública, no de una captura. Antes de la prueba hay que comparar contra un mensaje real de cada uno (ver `preparativos.md`).

## Etapa 2 — Ingesta en el servidor · HECHA

- API en ASP.NET Core 10: `POST /api/v1/eventos` por lotes de hasta 500.
- Token por equipo, guardado como hash; los tokens se generan con un comando y se pasan por variable de entorno.
- Idempotente por `eventId`: un reenvío no duplica nada.
- Responde con la hora del servidor para que el equipo mida el desvío de su reloj.
- Base: EF Core 10 con SQL Server, igual que APPro. SQLite solo para correr en una notebook.
- Tarea programada que reemplaza la patente en texto por un hash después de N días (configurable).

**Queda listo:** servidor que recibe y guarda eventos, con migraciones de base.

Hecho en `src/Bravo.Video.Ingesta` (EF Core 10.0.14, migración `Inicial` para SQL Server; SQLite crea las tablas desde el modelo). 12 pruebas de integración más una prueba manual con curl contra el servidor real.

- La respuesta del lote lista los rechazados con índice y motivo. Un evento malo no frena al resto del lote.
- Se guarda en columnas, sin el JSON original, para que anonimizar sea un UPDATE verificable.
- El hash de la patente se calcula al recibir el evento. Después de anonimizar queda solo el hash, y las estadías se siguen pudiendo emparejar.
- El plazo de retención **no tiene valor por defecto**. Sin `VIDEO_RETENCION_PATENTE_DIAS` el servidor no arranca: lo tiene que definir el Owner.
- Se fijó `System.Security.Cryptography.Pkcs` en 10.0.14 porque `Microsoft.Data.SqlClient` 6.1.7 trae la 9.0.11, que tiene vulnerabilidades conocidas.

## Etapa 3 — Conector del local · HECHA


- Servicio .NET 10 que corre en el mini PC del local.
- Adaptadores:
  - Frigate por MQTT (MQTTnet).
  - Cámara ANPR por HTTP (la cámara le envía cada lectura).
  - Sensor de conteo por HTTP.
- Cada cámara se configura con su rol: ingreso, egreso, o por sentido de circulación.
- Cola en disco: si se corta internet, guarda y reenvía después.
- Latido cada minuto con estado de motores, cola pendiente y desvío de reloj.
- **No** sube imágenes y **no** escribe patentes en los logs.

**Queda listo:** conector configurable por archivo y variables de entorno.

Hecho en `src/Bravo.Video.Conector` (MQTTnet 5.2.0.1603). Ejemplo de configuración en `config/conector.ejemplo.json`. 19 pruebas, entre ellas una con un broker MQTT real en memoria y otra con la cámara mandando multipart con imagen. Prueba de punta a punta con los dos procesos reales: con la ingesta caída la lectura queda en cola; al levantarla se entrega en 5 s, sin duplicar y sin patentes en los logs.

- **Cola:** un archivo por evento con escritura atómica, para inspeccionarla con `ls`. Si se llena, rechaza lo nuevo y lo cuenta; no pisa lo viejo. Un lote que la ingesta rechaza entero va a `cuarentena/` para no trabar la cola.
- **Cámaras ANPR y sensores:** no pueden mandar token, así que se aceptan solo desde la IP configurada para cada uno. Las imágenes del multipart se leen y se descartan sin tocar el disco.
- **MQTT con sesión limpia:** si el conector estuvo caído, los mensajes de ese rato no se reciben; quedan en la grabación de Frigate. Decisión a revisar si la prueba muestra cortes frecuentes.
- **Equipos que solo mandan con tránsito (ANPR, sensor):** el latido los informa como `desconocido`, y no como caídos, si no mandaron nada en la última hora. De noche el silencio es normal.

## Etapa 4 — Tablero · ESPERA DECISIÓN (ver PR #1)


- Página web servida por la ingesta: ingresos y egresos por hora, estadías (promedio y mediana), vehículos sin pareja, conteo de personas y estado de cada equipo.
- Exportación CSV de lecturas para etiquetar la muestra a mano.

**Decisión pendiente del Owner, pedida en el PR #1:** el tablero se propone solo con números, sin patentes. La exportación necesita las patentes en texto y no se construye hasta que Matías la apruebe por escrito.

## Etapa 5 — Herramientas de prueba · HECHA

- **Simulador:** genera autos con patentes inventadas que entran, estacionan y salen. Publica en el MQTT como lo haría Frigate y llama al conector como lo haría la cámara ANPR. Permite probar todo sin cámaras.
- **Evaluador:** cruza las lecturas con la muestra etiquetada y calcula % de patentes bien leídas (día y noche por separado), errores más comunes y error de conteo.

Hecho en `src/Bravo.Video.Simulacion`, `src/Bravo.Video.Evaluacion` y la línea de comandos `bravo-video` (`src/Bravo.Video.Herramientas`). 21 pruebas. Script de punta a punta en `scripts/prueba-punta-a-punta.sh`: simulador → Mosquitto 2.0.18 → conector → ingesta, con corte de la ingesta en el medio. Última corrida: 390 de 390 eventos entregados, sin duplicados ni patentes en los logs, en 31 s.

- **Patentes inventadas con prefijo Z** (AR: `ZV`…`ZZ`). En Argentina esa serie no se va a emitir en décadas.
- **Ruido configurable por período:** sin detección, sin lectura, error corregible por parecido y error no corregible. Con los valores por defecto da alrededor de 92% de día y 79% de noche, solo para probar el evaluador. **No es una predicción del motor real.**
- **El porcentaje principal usa como base todas las pasadas con patente legible**, aunque el motor no las haya detectado. Por separado se informa el porcentaje sobre las detectadas.
- **Intervalo de confianza de Wilson al 95%.** El veredicto contra el 95% puede ser "cumple", "no cumple" o "no se puede afirmar con esta muestra".
- **El evaluador recalcula la normalización desde el texto leído,** así mide el normalizador vigente.
- **El informe no lleva patentes,** solo cantidades y confusiones de carácter.
- **Formatos de planilla:** `etiquetas.csv` (`eventId,ts,patenteReal,periodo`, con `?` para ilegible y `-` para "no había vehículo") y `lecturas.csv`. Con datos reales son archivos privados.

## Etapa 6 — Instalación · HECHA (falta validar Frigate en el equipo real)

- `docker-compose` del local: Frigate 0.17.1 + Mosquitto 2.0.18 + conector.
- `docker-compose` del servidor: ingesta + SQL Server (o conexión al SQL Server existente).
- Plantilla de configuración de Frigate: lectura de patentes encendida; reconocimiento facial, IA generativa y envíos externos apagados.
- Guía de instalación paso a paso para el mini PC con Ubuntu 24.04.

Hecho en `deploy/` y `docs/instalacion-local.md` / `docs/instalacion-servidor.md`. Imágenes fijadas y verificadas en sus registros: `frigate:0.17.1`, `eclipse-mosquitto:2.0.18`, `dotnet/aspnet:10.0.14-noble`, `dotnet/sdk:10.0.403-noble` (trae el runtime 10.0.14) y `mssql/server:2022-CU27-ubuntu-22.04` (Express, solo para probar sin el SQL Server de la compañía).

- **Verificado:** los dos `docker-compose` pasan `docker compose config`, y si falta una variable obligatoria el error dice cuál. La publicación de los dos servicios funciona con solo lo que entra al contexto de Docker.
- **No verificado:** las imágenes no se pudieron construir porque este entorno no tiene daemon de Docker. Tampoco la plantilla de Frigate, que está escrita desde su documentación y se valida al arrancar en el mini PC (paso descrito en la guía).
- **Nueva opción `zonaRequerida` en el conector:** con cámaras de rol fijo, solo cuenta lo que pasa por la zona `carril` de Frigate. Un auto estacionado a la vista no suma.
- **Sin valor por defecto:** `record.retain.days` dice `PENDIENTE_OWNER` y Frigate no arranca hasta que se complete, igual que la ingesta sin plazo de retención.
- **Apagado en Frigate:** reconocimiento facial, búsqueda semántica, IA generativa, snapshots y chequeo de versión.
- **Puertos:** Mosquitto queda sin puerto publicado. La ingesta escucha solo en 127.0.0.1 y el TLS lo pone el proxy de infraestructura.

## Etapa 7 — Verificación

- Tests automáticos con xUnit, solo con datos inventados.
- Prueba de punta a punta con el simulador: simulador → conector → ingesta → tablero, incluyendo corte de red y reenvío.
- Revisión independiente del código antes de entregarlo.

## Etapa 8 — Entrega

- Push al repo de GitHub con README, guía de instalación, guía de evaluación y documentación del modelo de visión: qué datos recibe, qué puede hacer y qué límites tiene.

## Etapa 9 — Prueba en la empresa · con ustedes

1. Instalar el mini PC y conectar las cámaras.
2. Calibrar zonas y umbrales en Frigate con una tarde de tránsito real.
3. Dos semanas de captura.
4. Etiquetar la muestra y correr el evaluador.
5. Decidir el motor de patentes con datos (Frigate o cámara ANPR) y pasar a México.

---

## Lo que bloquea cada etapa

| Etapa | Bloqueo | Quién lo destraba |
| --- | --- | --- |
| 1 | Ninguno (hecha) | — |
| 2 | Ninguno (hecha) | — |
| 3 | Ninguno (hecha) | — |
| 4 | Decisión del Owner sobre patentes en tablero y exportación | Matías (en el PR #1) |
| 5 | Ninguno (hecha) | — |
| 6 | Hecha en código; instalar espera la aprobación de Python y el plazo de los recortes | Owner |
| 7 | Ninguno | — |
| 8 | Repo definitivo de GitHub (hoy vive en `GRUPOAISLAR/Appro`) | Gastón (crear repo y pasarme el nombre) |
| 9 | Aprobación de Python, legal, hardware, servidor | Owner, empresa e infraestructura |
