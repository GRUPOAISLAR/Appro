# APPro — frente Grupo AISLAR × Bravo SM

Repo de trabajo del frente **APPro**, entre **Grupo AISLAR** (`GRUPOAISLAR`) y
**Bravo SM / Brand Voice Group** (`bravosm.com`) con el equipo de desarrollo BravoDevArg.

## Arranque

| Sos… | Leé |
|---|---|
| Un agente de IA (Claude Code, Codex, Gemini) | **`CLAUDE.md`** — y nada más hasta terminarlo |
| Codex / Gemini específicamente | `AGENTS.md` |
| Alguien que va a montar el acceso | `docs/SETUP-BRAVOSM.md` |
| Alguien que necesita saber qué está conectado | `docs/ENTORNO-Y-CONEXIONES.md` |

## Qué es esto

El paquete de arranque del frente: quién es el interlocutor, cómo se trabaja, qué se puede tocar
y qué no. La idea es que una sesión nueva rinda como una sesión número veinte, sin re-descubrir
nada y sin repetir explicaciones.

## Alcance de la fase 1

**Solo lectura.** Conectores `GET` contra réplicas BI (nunca contra bases online), OCR que
devuelve JSON validado, y BYO-key: cada cliente pone su propia API key. La escritura es fase 2
y va con gate explícito.

El know-how de OCR de comprobantes que alimenta este frente —24 aprendizajes de ~2 años de
facturas argentinas en producción— está en `_PLAN_OCR_GENERICO_GEMINI_APPPRO_2026-08-10.md`
del repo privado `GRUPOAISLAR/continuidad-aislar`.
