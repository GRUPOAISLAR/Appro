# Análisis de video — Lo que hay que dejar preparado

Fecha: 4 de noviembre de 2026.

Ordenado por urgencia. Lo primero destraba mi trabajo; lo demás hace falta para instalar en la empresa.

---

## 1. Para que pueda seguir construyendo (ya)

- [x] **Habilitar NuGet.** Hecho (verificado el 4/11).
- [ ] **Crear el repo en GitHub.** Vacío, privado, nombre sugerido `appro-video-analitica`. Pasarme `organización/nombre`. Mientras tanto el código vive en `GRUPOAISLAR/Appro/video-analitica/`.
- [ ] **Darle acceso a Claude a ese repo.** La app de GitHub de Claude tiene que tener permiso sobre el repo nuevo para que pueda hacer push.

## 2. Aprobaciones internas

- [ ] **Matías (Owner):** aprobar la excepción de Python para usar Frigate en el equipo del local.
- [ ] **Owner:** decidir dónde se guardan los eventos. Para la prueba alcanza con una base nueva (por ejemplo `VideoAnalitica`) en un SQL Server de la compañía.
- [ ] **Owner:** plazo de retención de la patente en texto antes de pasarla a hash (propuesta: 30 días) y de los recortes en el local.

## 3. Empresa de la prueba en Argentina

### Legal

- [ ] **Acuerdo escrito:** la empresa es la responsable de los datos y BraVo los procesa por su cuenta. Incluye finalidad y plazos.
- [ ] **Cartel de videovigilancia** con el modelo de la AAIP, visible en ingreso y egreso.
- [ ] **Base de videovigilancia declarada** ante la AAIP, con su manual de tratamiento (si la empresa todavía no la tiene).
- [ ] **Aviso escrito a los empleados** de qué se filma y para qué.

### Cámaras

Con esto evalúo si la ubicación actual sirve para leer patentes.

- [ ] Marca, modelo y resolución de las cámaras de ingreso y egreso.
- [ ] Acceso RTSP con dos flujos (principal y secundario) y un **usuario de solo lectura** para nosotros.
- [ ] Una captura del flujo principal con un auto en el ingreso y otra en el egreso, **de día y de noche**.
- [ ] **Un mensaje real de cada motor** para validar los lectores: un `frigate/events` y un `frigate/tracked_object_update` de tipo `lpr` (Frigate 0.17.1), una alerta XML de la cámara ANPR y un reporte del sensor de conteo. Se usan solo para comparar los nombres de los campos y no van al repo. Si se quieren dejar como prueba, hay que reemplazar antes las patentes por inventadas.
- [ ] Distancia y altura aproximadas de la cámara al punto donde pasa el auto.
- [ ] Si el auto frena en ese punto (barrera o garita) y cuántos carriles hay.

Las capturas tienen patentes reales: se usan solo para esta evaluación y no van al repo ni a documentos.

### Referencia para medir

- [ ] **Registro de ingresos que ya tengan** (guardia, barrera o planilla) y en qué formato está. Sirve para comparar.
- [ ] **Una persona que etiquete a mano una muestra** de unas 200 pasadas, mitad de día y mitad de noche. Con 200 se mide el criterio de 95% con un margen de unos ±3 puntos.

### Contacto

- [ ] Persona de contacto en el lugar y horarios en que se puede entrar a instalar.

## 4. Hardware y red en la empresa

- [ ] **Mini PC:** Intel N100 o Core i5, 16 GB de RAM, SSD de 256 a 512 GB, con Ubuntu 24.04 LTS.
- [ ] **Energía:** enchufe con UPS para el mini PC y el switch.
- [ ] **Red:** punto cableado en la misma red que las cámaras, con IP fija reservada para el mini PC y para cada cámara.
- [ ] **Salida a internet:** solo HTTPS (puerto 443) hacia nuestro servidor.
- [ ] **Acceso remoto para soporte:** VPN o el túnel que apruebe la empresa.
- [ ] **Hora sincronizada (NTP)** en el mini PC y en las cámaras.
- [ ] **Si las cámaras actuales no sirven:** una cámara de patentes dedicada. Antes de comprarla, el proveedor tiene que mostrar que envía las lecturas por HTTP con ese modelo y firmware.

## 5. Servidor en nuestra infraestructura

- [ ] **Máquina o VM Linux con Docker** para la ingesta, o lugar dentro de la infraestructura de APPro.
- [ ] **SQL Server:** base nueva y un usuario con permisos solo sobre esa base.
- [ ] **Subdominio con certificado TLS** accesible desde la empresa (por ejemplo `video-ingesta.<dominio de BraVo>`).
- [ ] **Secretos en el servidor**, nunca en el repo: cadena de conexión, clave para el hash de patentes y token del tablero.

## 6. Para México (después de Argentina)

- [ ] Sede piloto y acuerdo de colaboración con el taller.
- [ ] Abogado en México para validar la LFPDPPP 2025, aviso de privacidad y contrato.
- [ ] Datos de cámaras y NVR existentes, con capturas de cada vista.
- [ ] Compra de cámara de patentes y sensor de conteo, según lo que resulte en Argentina.

---

## Quién hace cada cosa

| Quién | Qué |
| --- | --- |
| Gastón | Repo de GitHub y acceso de Claude |
| Matías (Owner) | Python, dónde se guardan los eventos, plazos de retención |
| Empresa de la prueba | Acuerdo, cartel, declaración AAIP, aviso a empleados, datos de cámaras, contacto |
| Infraestructura | Mini PC, red, servidor, SQL Server, subdominio y secretos |
