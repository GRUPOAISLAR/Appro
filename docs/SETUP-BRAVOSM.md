# Cómo montar el entorno Claude Code para el frente Bravo SM

> Respuesta a la pregunta "¿hago un usuario Claude Code aparte para Bravo SM, o hay una mejor forma?"
>
> **Resumen: depende de quién opera el agente.** Y en los dos casos, lo que realmente acorta el
> desarrollo no es la cuenta — es este paquete de `.md`.

---

## 1. Lo primero: qué aísla y qué no

Hay tres cosas que se confunden fácil, y conviene tenerlas separadas en la cabeza:

| Capa | Qué controla | ¿Aísla de un tercero? |
|---|---|---|
| **Cuenta** de Claude | Conectores MCP (Gmail, Drive, Home Assistant, Make…), skills, facturación | ✅ **Sí. Es el único aislamiento real** |
| **Environment** | Red, variables de entorno, scripts de arranque | ❌ No. Comparte los conectores de la cuenta |
| **Repo + `CLAUDE.md`** | Qué código ve el agente y con qué criterio trabaja | Parcial: aísla el código, no los conectores |

**La trampa:** crear un environment nuevo bajo mi cuenta *parece* aislamiento, pero no lo es.
Los conectores están a nivel cuenta. Un agente corriendo ahí sigue teniendo alcance a mi Gmail,
mi Drive y mi Home Assistant. Para un tercero eso no va.

---

## 2. Los dos escenarios

### Escenario A — El agente lo opera **Bravo SM** (Juan Valacka, `integ@bravosm.com`, el equipo de APPro)

**→ Cuenta Claude propia de ellos, pagada por ellos.** Sin discusión.

- Yo no les doy acceso a mi cuenta. No es desconfianza: es que mi cuenta tiene el Gmail de la
  empresa, el Drive con exports reales y tres servidores de Home Assistant de mi casa colgando.
- Lo que **sí** les entrego es este repo: `CLAUDE.md` + `docs/`. Eso es el acelerador.
- Acceso al código: son colaboradores de `GRUPOAISLAR/Appro` y **solo** de ese repo.

**Pasos:**
1. Ellos crean su cuenta y conectan **sus** integraciones (su GitHub, su Vercel si aplica).
2. Los agrego como colaboradores del repo `Appro`. Nada más.
3. Clonan el repo. Claude Code lee `CLAUDE.md` solo, en la primera sesión.
4. Las reglas de fase 1 (read-only, BYO-key, nunca contra bases online) ya están escritas ahí:
   no hay que repetirlas en cada sesión.

### Escenario B — El agente lo opero **yo**, sobre el frente APPro

**→ No hagas una cuenta nueva.** No hace falta y te cuesta plata y continuidad.

- Un **environment aparte** + el repo `Appro` con su propio `CLAUDE.md` alcanza.
- Y en cada chat de ese frente, **apagá los conectores que no corresponden**: Home Assistant (×3),
  Gmail, Calendar, Drive, Make. Dejá GitHub, y Supabase/Vercel solo si el piloto los usa.
  El listado de qué sí y qué no está en `ENTORNO-Y-CONEXIONES.md §1`.
- Ganás: seguís teniendo tu historial, tus skills y una sola suscripción.

---

## 3. La recomendación concreta

**Escenario A para ellos, Escenario B para vos.** No es un punto medio: son dos entornos con
dueños distintos que comparten un solo artefacto, este repo.

Y el artefacto es el que importa. Lo que acorta el arranque de una sesión de cero no es la
configuración de la cuenta — es que el agente lea, en el primer segundo:

- quién sos y cómo querés que te hablen,
- qué se puede tocar y qué no,
- cuál es el gate de producción,
- y dónde quedó lo de la sesión anterior.

Eso ya está probado: es exactamente lo que hace `GRUPOAISLAR/continuidad-aislar` con
`_ESTADO-PROYECTOS.md`, `_ORGANIZACION-IA.md` y `AGENTS.md`. Este repo replica ese patrón para
el frente APPro.

---

## 4. Lo que hay que mantener al día

| Archivo | Cuándo tocarlo |
|---|---|
| `CLAUDE.md` | Cambia una regla de trabajo, un gate, o un interlocutor |
| `docs/ENTORNO-Y-CONEXIONES.md` | Se conecta o desconecta un servicio, o cambia una cuenta |
| `docs/SETUP-BRAVOSM.md` | Cambia el modelo de acceso o entra gente nueva |
| `AGENTS.md` | Solo si cambia el arranque para Codex / Gemini |

Regla práctica: si en una sesión tuviste que **explicar** algo que ya habías explicado antes,
eso va a un `.md`. Es la señal.
