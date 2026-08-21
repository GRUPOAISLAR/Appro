# CLAUDE.md — Arranque del frente APPro / Bravo SM

> **Si sos un agente nuevo (Claude Code, Codex o Gemini CLI): leé este archivo entero antes de tocar nada.**
> Está escrito para que no tengas que re-descubrir quién soy, cómo trabajo ni con qué sistemas
> me conecto. Objetivo: que la primera sesión rinda como la número veinte.
>
> Última actualización: 2026-08-21.

---

## 1. Quién soy

**Gastón Borges.** Dirijo la operación y la automatización de **Grupo AISLAR** (aislaciones y
cubiertas, Argentina). Trabajo desde Buenos Aires, zona horaria `America/Argentina/Buenos_Aires`,
en **español rioplatense**. Escribime y contestame igual: directo, sin rodeos, sin adular.

- Identidad GitHub: `GRUPOAISLAR` — https://github.com/GRUPOAISLAR
- Mail de trabajo: `gaston.borges@aislarweb.com.ar` · personal: `gastonborges@gmail.com`
- No soy dev de profesión: soy el dueño del problema de negocio que dirige a los agentes.
  Explicame el **qué** y el **por qué** antes que el detalle de implementación, pero no me
  escondas los riesgos técnicos: los quiero explícitos.

### Cómo laburo con IA (esto es lo que más te conviene entender)

Alterno entre **Claude Code, Codex y Gemini CLI** según los límites de uso de cada uno.
Consecuencia directa, y es la regla que gobierna todo lo demás:

> **La memoria interna del agente NO es la fuente de verdad. Los archivos `.md` versionados sí.**

Si dejás algo a medias, dejalo **escrito** en el repo. El próximo agente puede no ser vos y
casi seguro no ve tu memoria.

---

## 2. Qué es este repo

`GRUPOAISLAR/Appro` es el frente de trabajo de **APPro** junto con **Bravo SM / Brand Voice Group**
(`bravosm.com`) y BravoDevArg — el equipo de desarrollo de APPro.

Contexto que ya existe y conviene no reinventar:

- El know-how de OCR de comprobantes (2 años de facturas argentinas reales en producción sobre
  CALIPSO) ya está transferido en el documento
  `_PLAN_OCR_GENERICO_GEMINI_APPPRO_2026-08-10.md` del repo privado `GRUPOAISLAR/continuidad-aislar`.
  Son 24 aprendizajes que corresponden, uno a uno, a un bug real que costó facturas mal cargadas.
- Alcance acordado de la fase 1: **solo lectura**. Conectores `GET` contra réplicas BI
  (nunca contra bases online), OCR que devuelve JSON, y **BYO-key** (cada cliente pone su propia
  API key). La escritura es fase 2 y va con gate explícito.
- Interlocutores del lado Bravo SM: Juan Valacka Bravo (BravoDevArg), Matías Borges
  (`matias@bravosm.com`), Ignacio Minardi, Antonella, Paola, `integ@bravosm.com`.

---

## 3. Mis reglas de trabajo (no negociables)

### 3.1 Git y despliegue

1. **Flujo de rutina = commit local → branch → push → PR.** Siempre. Está probado que funciona
   sin autorización puntual; el push directo a `main` no.
2. **El merge a `main` lo decido yo.** En varios repos el merge dispara producción.
3. **Nunca** `git reset --hard` ni force-push sobre un clon o rama que pueda tener trabajo de
   otra sesión. Ya me pasó, y duele.
4. **Un working tree, un agente.** No compartas árbol de trabajo con otra sesión en paralelo.
   Si abrís un worktree nuevo, documentalo (rama + qué hace + por qué).

### 3.2 Verificar antes de pushear

5. Corré los tests **pertinentes** antes de subir, y decime el resultado real. Si algo falla,
   quiero el output, no un resumen optimista.
6. Si un suite completo está roto o toca red/producción, **no lo corras a ciegas**: verificá
   por archivo. Prefiero una verificación parcial y honesta a una verde y falsa.

### 3.3 Plata y terceros

7. **Código que mueve plata** (tesorería, nómina, conciliación, compras, facturación):
   no confiar a ciegas nunca. Validar con un **movimiento de $1 real** antes de darlo por bueno.
8. **Sistemas de terceros: read-only por defecto.** Escribir requiere un gate explícito
   (variable de entorno tipo `*_APPLY=1`) y un OK mío en el momento.
9. **Mails a terceros: solo si te lo pido en esa sesión.** No los infieras ni los mandes
   "porque tenía sentido". Un borrador, sí. Enviar, no.

### 3.4 Datos y secretos

10. `.env`, credenciales, HARs crudos, PDFs y exports reales: **solo local, nunca a git.**
11. No hardcodees rutas absolutas en scripts nuevos. Rutas relativas al script o variables de
    entorno. Mover una carpeta no debería romper nada en silencio.

### 3.5 Continuidad

12. Al cerrar una tarea: tests → registrar qué quedó hecho y qué a medias en el `.md` que
    corresponda → commit + push + PR. **Con los hashes de los últimos commits.**
13. Si algo depende de mí (un clic, una credencial, una decisión), no lo intentes: anotámelo
    en un archivo de pendientes, en una línea, con lo que tengo que hacer exactamente.

---

## 4. Con qué me gusta trabajar (stack real)

| Capa | Herramienta | Cuenta / detalle |
|---|---|---|
| Código | GitHub `GRUPOAISLAR` | repos: Appro, Obras, CALIPSO, CALIPSO-kb, Obras-Android, Hassio, continuidad-aislar, ocr-facturas-aislar |
| Front / deploy | **Vercel** | team `gastonborges-1060s-projects` — proyectos `aislar-obras`, `aislar-obras-staging`, `lote101ha`, `finanzas-borges` |
| Datos | **Supabase** (org `GRUPOAISLAR's Org`) y **Firebase/Firestore** en Obras | Postgres 17 |
| Automatización | **Make** (`gaston.borges@aislarweb.com.ar`, org Free, 1 team) | escenarios de cámaras, Google Sheets, armado de planillas para Calipso |
| Domótica | **Home Assistant** — 3 servidores conectados: `Hassio Admin`, `Hassio Costa 204`, `Home Assistant` | personal, no mezclar con trabajo de terceros |
| Google | Gmail, Calendar, Drive | flujo diario de la operación |
| ERP | **CALIPSO** | vía API + Playwright, usuario `API_USER` |

Detalle completo, y qué corresponde habilitar o no en un entorno compartido con Bravo SM:
**`docs/ENTORNO-Y-CONEXIONES.md`**.

---

## 5. Formato de respuesta que prefiero

- **Español, directo.** Nada de "¡Excelente pregunta!". Andá al punto.
- Primero **qué hiciste / qué encontraste**, después el detalle.
- Si hay un riesgo, decilo en la primera línea, no en la última.
- Si no sabés algo, decí que no sabés. No inventes ni maquilles.
- Cuando termines algo, decime **cómo lo verificaste**. "Anda" sin evidencia no me sirve.

---

## 6. Qué NO hacer

- No mergear a `main` por tu cuenta.
- No mandar mails, mensajes de WhatsApp ni nada hacia afuera sin pedido explícito.
- No escribir en sistemas productivos de terceros (APPro incluido) en fase 1.
- No subir secretos, ni siquiera "temporalmente".
- No asumir que tu memoria de sesión sobrevive: lo que importa va al repo.

---

## 7. Para agentes que no son Claude

Ver `AGENTS.md` (Codex) y el mismo contenido aplica a Gemini CLI. Las reglas de este archivo
son las mismas para todos: cambia la herramienta, no el criterio.
