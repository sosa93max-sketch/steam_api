# steam_api handoff

## Identidad de la cuenta

El shim carga `steam_api.ini` desde `Common.DataPath()`, que corresponde a
`<dota2.exe dir>\D2MAX`. La ruta legacy `SKYNET` solo se migra cuando no existe
`D2MAX`; una vez creada la carpeta nueva, cambiar un archivo en `SKYNET` ya no
cambia la cuenta activa. Por eso el launcher debe escribir el `FallbackAccountId`
del perfil actual directamente en `D2MAX\steam_api.ini` antes de cada arranque.

Los datos persistentes que dependen de identidad (inventario y Workshop) deben
estar separados por `SteamId`/`AppId`. El shim no fija el usuario en código: lee
`FallbackAccountId` en cada proceso y el servidor puede reemplazar el Steam ID
cuando crea la sesión.

## Workshop y segundo arranque

El snapshot local de suscripciones ya se guarda fuera del árbol del juego, en
`%LOCALAPPDATA%\D2Max\Workshop\<AppId>\<SteamId>\subscriptions.json`. El shim
solo lee la ruta antigua `D2MAX\Workshop`/`SKYNET\Workshop` para migrarla una
vez y luego elimina el archivo y las carpetas que hayan quedado vacías. No borra
recursivamente contenido Workshop real del usuario.

La interfaz `SteamUGC` se crea durante el arranque crítico de Dota. La carga
normal solo consulta el snapshot externo; la lectura/migración de snapshots
legacy y la poda de carpetas vacías se encolan en `ThreadPool` después de
devolver la interfaz. Así una carpeta legacy grande, bloqueada o dañada no puede
dejar el cliente inicializado pero oculto en `Creating SteamUGC`.
La tarea en segundo plano resuelve `D2MAX` y `SKYNET` como rutas explícitas y no
llama a `Common.DataPath`, por lo que tampoco intenta mover todo el árbol legacy
mientras Dota está arrancando.

La implementación no crea una carpeta `Workshop` junto a `dota2.exe`. Esto evita
que Dota inspeccione una carpeta generada por el emulador y deje la segunda
instancia en segundo plano. También se añadió un cierre cooperativo: se marca la
cuenta offline, se cancelan los long-polls, se abortan las peticiones activas, se
despiertan y esperan los workers, se cierra el servidor TCP y se detienen Music y
HTML. El hook de `ProcessExit` cubre juegos que no llaman a
`SteamAPI_Shutdown`.

## Verificación

- `git diff --check` y revisión estática de las rutas pasan.
- El primer build de Windows del commit `491e4fd` reveló que `WorkQueue` no
  importaba `SKYNET.Helpers` y que convenía separar la condición de cancelación
  del `TryDequeue(out var ...)` para el compilador .NET Framework; ambos puntos
  quedan corregidos en el commit de seguimiento.
- En Windows: borrar una carpeta `D2MAX\Workshop` antigua solo después de
  conservar cualquier contenido real; iniciar con cuenta A, cerrar Dota, iniciar
  con cuenta B y confirmar el `FallbackAccountId` en
  `game\bin\win64\D2MAX\steam_api.ini`.
- Confirmar en Task Manager que no queda `dota2.exe` de la primera ejecución y
  repetir el lanzamiento sin borrar manualmente la carpeta `D2MAX`.
