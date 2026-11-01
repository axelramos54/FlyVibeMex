<!--
  Plantilla de Pull Request de FlyVibeMex (T8.4). GitHub la carga sola al abrir un PR hacia main.
  Llena lo que aplique y borra lo que no. Un PR = una tarea del plan (docs/07_REVISION_Y_PLAN_DE_TRABAJO.md).
-->

## ¿Qué cambia?

<!-- 2 o 3 líneas: qué hace este PR y por qué. Ejemplo: "T8.1: el registro pasa a AuthService…" -->

**Tarea del plan:** T_._ <!-- p. ej. T8.3 -->
**Rama:** `feature/T_._-descripcion-corta` <!-- o fix/… para correcciones -->

## Cómo probarlo

<!-- Pasos concretos para que quien revise lo compruebe en su PC. -->
1.
2.

## Checklist antes de pedir revisión

- [ ] `dotnet build` termina **sin errores** (y sin warnings nuevos).
- [ ] Probé a mano las pantallas que toqué (con un usuario CLIENTE y, si aplica, con uno del panel).
- [ ] Revisé la vista en celular (F12 → 375 px) si cambié HTML o CSS.
- [ ] **Sin secretos:** no subo contraseñas, cadenas de conexión reales ni `appsettings.Production.json`
      (las cadenas van en `dotnet user-secrets`; la de producción solo en el servidor).
- [ ] Si cambié la base de datos: actualicé los scripts de `sql/` (01 esquema, 02 seed, 03 procedimientos) y lo
      anoto abajo para que el equipo vuelva a correrlos.
- [ ] Los formularios POST llevan token antiforgery y validan en el servidor (`ModelState.IsValid`).
- [ ] Actualicé `docs/07_REVISION_Y_PLAN_DE_TRABAJO.md` (tarea marcada) y, si aplica, el README u otro doc.
- [ ] Mi rama está al día con `main` (`git pull origin main`) y no tiene conflictos.

## Notas para quien revisa

<!-- Scripts SQL que hay que correr, decisiones tomadas, pendientes o capturas de pantalla. -->
