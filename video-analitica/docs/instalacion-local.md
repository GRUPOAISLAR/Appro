# Instalación en el local (mini PC)

Para la persona que instala el mini PC en la empresa de la prueba. Se hace una vez, en unas dos horas, más una tarde de calibración (Etapa 9).

> **Antes de empezar** tienen que estar resueltos los puntos 2 y 3 de `preparativos.md`: la aprobación de Python para Frigate, el plazo de retención de los recortes, el acuerdo con la empresa y el cartel. Sin eso no se instala.

## Qué se necesita

- **Mini PC:** Intel N100 o Core i5, 16 GB de RAM, SSD de 256 a 512 GB y UPS.
- **Red:** cable a la misma red que las cámaras, con IP fija reservada. Salida a internet solo por HTTPS (443) hacia el servidor de BraVo.
- **Cámaras:** usuario **de solo lectura** en cada cámara, con acceso RTSP.
- **Token del equipo:** lo genera quien administra el servidor con `equipo-alta`. Ver `instalacion-servidor.md`.
- **Hora sincronizada (NTP)** en el mini PC y en las cámaras. Sin esto las estadías salen corridas.

## 1. Sistema

Ubuntu Server 24.04 LTS, instalación mínima. Después:

```bash
sudo apt update && sudo apt upgrade -y
sudo timedatectl set-timezone America/Argentina/Buenos_Aires
timedatectl                  # tiene que decir "System clock synchronized: yes"
ls /dev/dri/renderD128       # GPU integrada: Frigate la usa para video y detección
```

## 2. Docker

Seguir la guía oficial de Docker Engine para Ubuntu (repositorio `download.docker.com`, paquetes `docker-ce` y `docker-compose-plugin`). Después:

```bash
sudo usermod -aG docker $USER   # cerrar sesión y volver a entrar
docker compose version
```

## 3. Código y configuración

```bash
sudo mkdir -p /opt/bravo-video /var/lib/bravo-video/frigate
sudo chown $USER /opt/bravo-video
# Copiar la carpeta video-analitica del repo a /opt/bravo-video (git clone o scp).
cd /opt/bravo-video/video-analitica/deploy/local

cp .env.ejemplo .env
nano .env                                   # usuario RTSP, IPs de las cámaras, token
chmod 600 .env

cp ../../config/conector.ejemplo.json conector/conector.json
nano conector/conector.json                 # sede, dispositivo, urlIngesta, cámaras
```

En `conector.json`:

- **`urlIngesta`:** el subdominio HTTPS del servidor de BraVo.
- **`mqtt.host`:** se deja `mosquitto` (es el nombre del servicio en Docker).
- **Cámaras de Frigate:** se llaman `ingreso` y `egreso`, como en `frigate/config.yml`.
- **Cámaras ANPR y sensores:** solo se agregan si existen. Cada uno lleva su IP, porque el conector rechaza lo que llegue de otra.

En `frigate/config.yml`:

- **`record.retain.days`:** cambiar `PENDIENTE_OWNER` por los días que definió el Owner.
- **Rutas RTSP:** si las cámaras no son Hikvision, cambiar `/Streaming/Channels/101` por la ruta del flujo principal de esa marca.

## 4. Arranque

```bash
docker compose up -d --build
docker compose ps                           # los tres servicios en "running"
docker compose logs -f conector             # "Conectado a MQTT…"
curl -s http://localhost:8080/salud         # colaPendiente, mqttConectado: true
```

### Validar la configuración de Frigate

La plantilla está escrita desde la documentación de Frigate 0.17 y no se pudo probar con el programa real. Frigate valida el archivo al arrancar:

```bash
docker compose logs frigate | grep -iE "error|invalid|validation"
```

Si un campo no existe en esta versión, el log dice cuál. Se corrige o se borra ese campo, y después `docker compose restart frigate`. **No** prender reconocimiento facial, búsqueda semántica, IA generativa ni snapshots sin decisión del Owner.

## 5. Calibración (con tránsito real)

1. Entrar a `https://<IP del mini PC>:8971`. El usuario admin y su contraseña inicial salen en `docker compose logs frigate`; cambiar la contraseña en el primer ingreso.
2. En cada cámara, dibujar la zona **`carril`**: el lugar por donde pasa el auto, sin incluir la playa de estacionamiento. Copiar las coordenadas a `frigate/config.yml`.
3. Mirar una tarde de tránsito. Ajustar `detect.fps` y, si hace falta, `lpr.recognition_threshold`.
4. En el servidor, verificar que llegan ingresos y egresos y que el latido marca todo `en_linea`.

## 6. Qué mirar después

| Qué | Dónde |
| --- | --- |
| Cola sin enviar | `curl -s localhost:8080/salud` → `colaPendiente` (con internet tiene que estar cerca de 0) |
| Lotes apartados | `enCuarentena` en `/salud`. Los archivos quedan en el volumen `cola`, carpeta `cuarentena/` |
| Desvío del reloj | `desvioRelojMs` en `/salud`. Si pasa de unos segundos, revisar NTP |
| Errores | `docker compose logs --since 1h conector` |

Los logs del conector no tienen patentes. La cola en disco sí, hasta que el servidor confirma la recepción.

## Datos que quedan en el mini PC

- **Grabación de Frigate** en `/var/lib/bravo-video/frigate`, durante los días configurados. Tiene imágenes de personas y vehículos y **no sale del local**.
- **Cola del conector** (volumen `cola`): patentes en texto, solo hasta que la ingesta las confirma.
- **No se guardan snapshots** ni imágenes de las cámaras ANPR. El conector las descarta al recibirlas.
