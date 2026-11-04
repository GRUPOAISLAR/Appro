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

## Etapa 2 — Ingesta en el servidor · SIGUIENTE

- API en ASP.NET Core 10: `POST /api/v1/eventos` por lotes de hasta 500.
- Token por equipo, guardado como hash; los tokens se generan con un comando y se pasan por variable de entorno.
- Idempotente por `eventId`: un reenvío no duplica nada.
- Responde con la hora del servidor para que el equipo mida el desvío de su reloj.
- Base: EF Core 10 con SQL Server, igual que APPro. SQLite solo para correr en una notebook.
- Tarea programada que reemplaza la patente en texto por un hash después de N días (configurable).

**Queda listo:** servidor que recibe y guarda eventos, con migraciones de base.

## Etapa 3 — Conector del local

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

## Etapa 4 — Tablero

- Página web servida por la ingesta: ingresos y egresos por hora, estadías (promedio y mediana), vehículos sin pareja, conteo de personas y estado de cada equipo.
- Exportación CSV de lecturas para etiquetar la muestra a mano.

## Etapa 5 — Herramientas de prueba

- **Simulador:** genera autos con patentes inventadas que entran, estacionan y salen. Publica en el MQTT como lo haría Frigate y llama al conector como lo haría la cámara ANPR. Permite probar todo sin cámaras.
- **Evaluador:** cruza las lecturas con la muestra etiquetada y calcula % de patentes bien leídas (día y noche por separado), errores más comunes y error de conteo.

## Etapa 6 — Instalación

- `docker-compose` del local: Frigate 0.17.1 + Mosquitto 2.0.18 + conector.
- `docker-compose` del servidor: ingesta + SQL Server (o conexión al SQL Server existente).
- Plantilla de configuración de Frigate: lectura de patentes encendida; reconocimiento facial, IA generativa y envíos externos apagados.
- Guía de instalación paso a paso para el mini PC con Ubuntu 24.04.

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
| 2 a 7 | Ninguno (NuGet ya habilitado) | — |
| 8 | Repo definitivo de GitHub (hoy vive en `GRUPOAISLAR/Appro`) | Gastón (crear repo y pasarme el nombre) |
| 9 | Aprobación de Python, legal, hardware, servidor | Owner, empresa e infraestructura |
