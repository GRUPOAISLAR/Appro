# Análisis de video — APPro

Lectura de patentes y conteo en ingreso, egreso y estacionamiento. Primero va la prueba técnica en Argentina y después el piloto en México.

> **Ubicación provisoria.** Esta carpeta vive en `GRUPOAISLAR/Appro` hasta que exista el repo
> definitivo (`appro-video-analitica`). Se va a mover con su historial.

- Plan y estado de cada etapa: [`docs/plan-por-etapas.md`](docs/plan-por-etapas.md)
- Qué hay que preparar fuera del código: [`docs/preparativos.md`](docs/preparativos.md)
- Contrato de eventos v1: [`esquemas/eventos-v1.schema.json`](esquemas/eventos-v1.schema.json). La fuente de verdad es `Bravo.Video.Contratos`; hay una prueba que verifica que el esquema y el código listen los mismos tipos.

## Estructura

| Proyecto | Qué hace | Dependencias |
| --- | --- | --- |
| `Bravo.Video.Contratos` | Eventos v1, JSON, id determinístico, validación | ninguna |
| `Bravo.Video.Patentes` | Normalización y validación AR/MX, hash de patentes | ninguna |
| `Bravo.Video.Lectores` | Mensajes de Frigate, cámara Hikvision ANPR y sensor Milesight → eventos | Contratos, Patentes |
| `Bravo.Video.Estadias` | Emparejar ingreso con egreso, promedio y mediana | Contratos |
| `Bravo.Video.Ingesta` | API `POST /api/v1/eventos`, base, tokens, anonimización | EF Core 10.0.14 (SQL Server / SQLite) |
| `Bravo.Video.Conector` | Servicio del mini PC: Frigate por MQTT, ANPR y sensor por HTTP, cola en disco, latido | MQTTnet 5.2.0.1603 |

.NET 10 (SDK 10.0.114). Las librerías de `Contratos` a `Estadias` no usan paquetes externos. Las pruebas usan xUnit 2.9.3.

```
dotnet test Bravo.Video.slnx
```

## Ingesta: cómo se corre

Toda la configuración va por variables de entorno del servidor, nunca en el repo:

| Variable | Qué es |
| --- | --- |
| `VIDEO_DB_PROVEEDOR` | `SqlServer` (prueba en la empresa) o `Sqlite` (solo notebook) |
| `VIDEO_DB_CONEXION` | Cadena de conexión a la base `VideoAnalitica` |
| `VIDEO_CLAVE_HASH_PATENTE` | Clave del hash de patentes en base64, de al menos 32 bytes. Si se cambia, se corta la continuidad de las estadías. |
| `VIDEO_RETENCION_PATENTE_DIAS` | Días que se guarda la patente en texto. Sin valor por defecto: lo define el Owner. |
| `VIDEO_ANONIMIZACION_INTERVALO_MINUTOS` | Opcional, 60 por defecto |

```
dotnet Bravo.Video.Ingesta.dll migrar
dotnet Bravo.Video.Ingesta.dll equipo-alta --dispositivo minipc-01 --sede AR-PRUEBA-01   # muestra el token una vez
dotnet Bravo.Video.Ingesta.dll                                                          # levanta la API
```

Las migraciones se generan con la herramienta local: `dotnet tool restore` y después
`dotnet ef migrations add <Nombre> --project src/Bravo.Video.Ingesta --output-dir Datos/Migraciones`.

## Conector: cómo se corre

En el mini PC del local. La instalación se describe en `conector.json` (ver `config/conector.ejemplo.json`) y los secretos van por entorno:

| Variable | Qué es |
| --- | --- |
| `VIDEO_CONECTOR_CONFIG` | Ruta de `conector.json` (por defecto, el directorio actual) |
| `VIDEO_CONECTOR_TOKEN` | Token que dio `equipo-alta` en el servidor |
| `VIDEO_MQTT_USUARIO` / `VIDEO_MQTT_CLAVE` | Solo si Mosquitto pide usuario |
| `ASPNETCORE_URLS` | Dónde escucha a las cámaras y sensores (ej. `http://0.0.0.0:8080`) |

Endpoints: `POST /anpr/{camara}` (cámara ANPR), `POST /conteo/{sensor}` (sensor de conteo), `GET /salud` (cola pendiente, cuarentena, MQTT, desvío del reloj).

## Reglas que no hay que romper

- **El id de un evento es determinístico** (`IdEvento.Derivar`). Hay una prueba con un valor fijado: si falla, no se "arregla" el valor esperado, porque cambiarlo hace que un reenvío duplique eventos en la ingesta.
- **Toda hora lleva zona horaria.** El JSON sin desplazamiento se rechaza: System.Text.Json le pondría en silencio la zona del servidor.
- **La normalización de patentes no tiene reglas implícitas.** Solo corrige los seis pares de `NormalizadorPatentes.Parecidos`, según la posición. Si hay empate entre formatos, la patente queda inválida: no se adivina. Cada corrección queda registrada.
- **Las estadías son trazables.** Cada una guarda los dos `eventId` que la forman, y cada evento suelto lleva su motivo.
- **Datos:** nada de patentes, imágenes ni capturas reales en el repo, en las pruebas ni en los logs. Las excepciones de los lectores nunca incluyen el contenido recibido.

## Qué datos maneja y qué no

- **Recibe** de cada motor el texto de la patente leída, la confianza, la cámara, la hora, y las zonas o el sentido de circulación. Del sensor de conteo recibe cantidades por línea.
- **No recibe ni guarda** imágenes, rostros ni datos de las personas. La configuración de Frigate deja apagado el reconocimiento facial (Etapa 6).
- **Patente en texto:** se guarda un plazo y después se reemplaza por un HMAC (`HashPatente`) con una clave del servidor. El plazo está pendiente de que lo decida el Owner.
