# Instalación del servidor (ingesta)

Para infraestructura de BraVo. Corre en una VM Linux con Docker y guarda en un SQL Server de la compañía.

## Qué se necesita

- **VM Linux con Docker y el plugin de compose.**
- **SQL Server:** una base `VideoAnalitica` y un usuario con permisos **solo** sobre ella. Mientras no exista, el perfil `sqlserver-local` levanta un SQL Server Express propio, solo para probar.
- **Subdominio con certificado TLS** (por ejemplo `video-ingesta.<dominio de BraVo>`), accesible desde la red de la empresa de la prueba.
- **Decisión del Owner** sobre el plazo de patente en texto (`VIDEO_RETENCION_PATENTE_DIAS`). Sin ese dato el servidor no arranca, a propósito.

## 1. Configuración

```bash
cd video-analitica/deploy/servidor
cp .env.ejemplo .env
chmod 600 .env
head -c 32 /dev/urandom | base64      # pegar en VIDEO_CLAVE_HASH_PATENTE
nano .env                             # cadena de conexión y días de retención
```

**Guardar `VIDEO_CLAVE_HASH_PATENTE` en el gestor de secretos de la compañía.** Si se pierde o se cambia, los hashes viejos no coinciden con los nuevos y se corta la continuidad de las estadías.

## 2. Base y equipo

```bash
docker compose build
docker compose run --rm ingesta migrar
docker compose run --rm ingesta equipo-alta --dispositivo minipc-01 --sede AR-PRUEBA-01
```

`equipo-alta` muestra el token **una sola vez**. Se le pasa a quien instala el mini PC por un canal privado, y va al `.env` del local. Para rotarlo: `equipo-alta … --reemplazar`. Para cortar el acceso: `equipo-baja --dispositivo minipc-01`.

## 3. Arranque y TLS

```bash
docker compose up -d
curl -s http://127.0.0.1:8080/salud    # {"estado":"ok"}
```

La ingesta escucha solo en `127.0.0.1:8080`. El proxy que ya use infraestructura (nginx, IIS, Traefik) publica el subdominio con certificado y reenvía a ese puerto. Hay que permitir cuerpos de al menos 2 MB (lotes de 500 eventos).

## 4. Verificación con el simulador (opcional, antes de conectar el local)

Desde cualquier máquina con .NET 10 y el repo:

```bash
scripts/prueba-punta-a-punta.sh        # todo en 127.0.0.1, no toca este servidor
```

## Qué guarda

- **Tabla `Eventos`:** un evento por fila, sin imágenes. La patente en texto se borra a los N días y queda solo el hash (tarea interna, cada 60 minutos).
- **Tabla `Equipos`:** el SHA-256 del token, nunca el token.
- **Logs:** solo cantidades por lote. Nunca patentes.
