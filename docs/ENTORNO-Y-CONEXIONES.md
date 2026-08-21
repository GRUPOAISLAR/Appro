# Entorno y conexiones — inventario real

> Qué tengo conectado hoy, para qué lo uso, y **qué corresponde habilitar o no** en un entorno
> que comparta trabajo con Bravo SM / BravoDevArg.
>
> Verificado el 2026-08-21 contra las conexiones vivas de la cuenta.

---

## 1. Conectores MCP activos

| Conector | Estado | Para qué lo uso | ¿En el entorno Bravo SM? |
|---|---|---|---|
| **GitHub** | conectado | Todo el código de `GRUPOAISLAR`. Es la fuente de verdad. | ✅ Sí, pero **solo el repo `Appro`** |
| **Vercel** | conectado | Deploy de las apps. Team `gastonborges-1060s-projects`. | ⚠️ Solo si APPro despliega ahí. Hoy, no |
| **Supabase** | conectado | Org `GRUPOAISLAR's Org`, proyecto Postgres 17 (hoy `INACTIVE`). | ⚠️ Solo si se usa para el piloto |
| **Make** | conectado | Escenarios de automatización (cámaras, Sheets, planillas Calipso). | ❌ No |
| **Gmail** | conectado | Operación diaria de AISLAR. Contiene todo el negocio. | ❌ **No.** Nunca en un entorno con terceros |
| **Google Calendar** | conectado | Agenda de obra y reuniones. | ❌ No |
| **Google Drive** | conectado | Documentos, planillas, HARs, exports. | ❌ No |
| **Home Assistant** (`Hassio Admin`) | conectado | Domótica — administración. | ❌ No. Es personal |
| **Home Assistant** (`Hassio Costa 204`) | conectado | Domótica — casa. | ❌ No. Es personal |
| **Home Assistant** (base) | conectado | Control de dispositivos. | ❌ No. Es personal |
| Cloudflare Developer Platform | instalado, sin conectar | — | — |
| Notion | instalado, sin conectar | — | — |
| Strava | instalado, sin conectar | — | — |

**El punto que importa:** los conectores viven a nivel **cuenta**, no a nivel proyecto. Un agente
que corre bajo mi cuenta tiene, en principio, alcance a mi Gmail, mi Drive y mi casa. Eso no se
resuelve creando otro *environment* — se resuelve con **otra cuenta** o **apagando conectores por
chat**. Ver `SETUP-BRAVOSM.md`.

---

## 2. Cuentas e identidades

| Servicio | Identidad |
|---|---|
| GitHub | `GRUPOAISLAR` (id 63421334) |
| Make | GASTON — `gaston.borges@aislarweb.com.ar`, org "My Organization" (plan Free), team "My Team" |
| Vercel | team `gastonborges-1060s-projects` (`team_QSjeWqGfsJCORzsEJy8FwVsv`) |
| Supabase | org `ldfcduxfavuegauacvht` — "GRUPOAISLAR's Org" |
| Google | `gaston.borges@aislarweb.com.ar` (trabajo) · `gastonborges@gmail.com` (personal) |
| CALIPSO (ERP) | usuario `API_USER` para API y navegación Playwright |

---

## 3. Repositorios

| Repo | Visibilidad | Qué es |
|---|---|---|
| `GRUPOAISLAR/Appro` | público | **Este.** Frente APPro / Bravo SM |
| `GRUPOAISLAR/Obras` | privado | PWA AISLAR Obras: fichajes, calendario/recursos, finanzas, rendición. Deploy en Vercel desde `main` |
| `GRUPOAISLAR/CALIPSO` | privado | Backend `agente_calypso` + cockpit. OCR, conciliación, tesorería, compras, asistente WhatsApp/Telegram |
| `GRUPOAISLAR/CALIPSO-kb` | privado | Base de conocimiento de APIs y flujos de Calipso |
| `GRUPOAISLAR/continuidad-aislar` | privado | **Los `.md` de continuidad entre sesiones y entre agentes.** El modelo que este repo replica |
| `GRUPOAISLAR/Obras-Android` | privado | Wrapper Android de Obras |
| `GRUPOAISLAR/ocr-facturas-aislar` | privado | OCR de facturas |
| `GRUPOAISLAR/Hassio` | público | Configuración de Home Assistant |

---

## 4. Skills habilitadas en mi cuenta Claude

`morning` (brief diario) · `skill-creator` · `xlsx` · `pptx` · `pdf` · `docx`.

Las de ofimática las uso seguido: mucho de lo que produzco termina en una planilla o un PDF para
alguien de la empresa.

---

## 5. Entorno de ejecución (Claude Code on the web)

- Environment: **`Default`** (`env_01MpohdPytSeSugAat8L6ZXw`) — acceso de red confiable,
  `anthropic_cloud`.
- Es el único environment que tengo. Si Bravo SM va a trabajar con un environment propio,
  hay que crearlo — ver `SETUP-BRAVOSM.md`.
