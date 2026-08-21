# AGENTS.md — arranque para Codex, Gemini CLI y cualquier otro agente

Este frente lo trabajan varios agentes de IA, y se alterna entre ellos según los límites de uso
de cada uno. **La memoria interna de un agente no la ven los otros.** Por eso la fuente de verdad
compartida son los archivos `.md` de este repo.

**Leé `CLAUDE.md` (raíz). Las reglas son las mismas para todos: cambia la herramienta, no el criterio.**

Después, según qué vayas a hacer:

- `docs/ENTORNO-Y-CONEXIONES.md` — qué servicios existen, con qué cuenta, y cuáles NO tocar.
- `docs/SETUP-BRAVOSM.md` — cómo está montado el acceso y por qué.

Al terminar: tests → dejá escrito qué quedó hecho y qué a medias → commit → branch → push → PR.
Con los hashes de los últimos commits.
