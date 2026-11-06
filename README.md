# TrucoRDR2 — prototipo jugable de truco argentino

Este proyecto C# compila como `TrucoRDR2.dll` para ScriptHookRDR2DotNet V2.2.0 / .NET Framework 4.8.

**Alcance de esta build:** carga y prueba las 40 texturas y ofrece una partida mano a mano de truco argentino con tanteador, apuestas de envido y truco, flor opcional, y un rival con estrategia básica. La IA es heurística y todavía necesita ajustes a partir de las pruebas dentro del juego.

## Requisitos

- RDR2 para PC en modo Historia.
- ScriptHookRDR2 de Alexander Blade.
- Community ScriptHookRDR2DotNet V2.2.0 (usa la `ScriptHookRDRNetAPI.dll` de esa misma instalación).
- .NET Framework 4.8 Developer Pack / targeting pack de Visual Studio.
- Lenny's Mod Loader para que el YTD personalizado se cargue como recurso independiente.

No se incluye ni redistribuye `ScriptHookRDRNetAPI.dll`; el proyecto debe referenciar la DLL instalada con ScriptHookRDR2DotNet.

## Configurar el YTD

1. Usá la herramienta **RedM / RDR2**: https://redm.fivemad.com/tools/folders-to-ytd. No uses la versión FiveM (`fivemad.com`), que genera diccionarios para GTA V y no para RDR2. La versión RedM genera diccionarios RDR2 y entrega un ZIP de recurso. Extraé el ZIP y copiá solamente el archivo `.ytd` que está dentro de `stream`; para LML no necesitás `fxmanifest.lua` ni el README del recurso.

   Para esta build, el nombre debe ser `trucor2_cards.ytd` y la ruta manual es:

   `D:\Red Dead Redemption 2\lml\stream\trucor2_cards.ytd`

   Creá `lml\stream` si no existe. No lo dejes en `lml\TrucoRDR2\stream` salvo que esa carpeta tenga un `install.xml` válido que registre el contenido.

2. Abrí `scripts/TrucoRDR2.ini` y confirmá que la sección contiene:

   ```ini
   [Textures]
   TextureDictionary=trucor2_cards
   ```

   La combinación para abrir la mesa se configura en la sección `[Controls]` del mismo archivo INI; el valor predeterminado es `Control+F9`.

3. Las cartas deben tener estos nombres de textura, sin `.png`: `01-bastos`, `01-copas`, `01-espadas`, `01-oros`, etc. El catálogo usa rangos `01`–`07` y `10`–`12` y omite 8 y 9, que no se usan en el truco argentino.

   Si generaste el YTD con RDR2 Texture ToolKit, usá el archivo de salida que termina en `_nya.ytd`; renombrá esa variante a `trucor2_cards.ytd`. El ToolKit también puede generar otro `.ytd` auxiliar que no es el destinado a RDR2.

## Compilar en Visual Studio 2022

1. Abrí `TrucoRDR2.csproj`.
2. Si RDR2 no está en la ruta predeterminada, editá la propiedad `RDR2Dir` dentro del `.csproj`, o compilá por línea de comandos:

   ```bat
   msbuild TrucoRDR2.csproj /p:Configuration=Release /p:Platform=x64 /p:RDR2Dir="D:\\Juegos\\RDR2"
   ```

   La carpeta indicada debe contener `ScriptHookRDRNetAPI.dll`. También se puede indicar la ruta exacta a esa DLL:

   ```bat
   msbuild TrucoRDR2.csproj /p:Configuration=Release /p:Platform=x64 /p:ScriptHookRDRNetAPIDll="D:\\Juegos\\RDR2\\ScriptHookRDRNetAPI.dll"
   ```

3. El resultado se genera en `bin/Release/TrucoRDR2.dll`.
4. Copiá `TrucoRDR2.dll` y `TrucoRDR2.ini` a `RDR2/scripts/`.

## Probar dentro del juego

- `F7`: mostrar u ocultar la vista de texturas. F6 queda libre porque abre el modo foto de RDR2. La tecla se procesa una sola vez hasta que se suelta, para evitar alternancias por repetición de teclado.
- `Av Pág` (`PageDown`): avanzar una carta en el catálogo.
- `Re Pág` (`PageUp`): retroceder una carta.
- `Esc`: cerrar la vista.

Los nombres de las tres texturas seleccionadas se muestran incluso cuando el YTD no está instalado o no se ha cargado. Las imágenes solo se dibujan cuando el juego confirma que el diccionario está cargado. La línea de estado permite separar problemas de registro del YTD de problemas con los nombres internos.

Al activar F7, el script solicita el diccionario antes de consultar si existe; esto evita bloquear la primera solicitud cuando el diccionario todavía no está en memoria. También crea `scripts\TrucoRDR2.log`, independiente del log de LML. Allí registra la ruta convencional esperada (`lml\stream\trucor2_cards.ytd`), si el archivo existe en disco, cuándo envía la solicitud y los resultados de `DOES_STREAMED_TEXTURE_DICT_EXIST` / `HAS_STREAMED_TEXTURE_DICT_LOADED`.

Si el estado muestra «El juego no encuentra…», abrí `scripts\TrucoRDR2.log` y comprobá primero `existe en disco: True`. Si muestra `False`, el archivo no está en la ruta esperada. Si muestra `True` pero el diccionario sigue sin aparecer, revisá que hayas generado el YTD con la herramienta RedM/RDR2, que se llame exactamente `trucor2_cards.ytd` y que LML esté instalado (incluido `vfs.asi` junto a `RDR2.exe`). El log estándar de LML suele estar en la carpeta raíz del juego como `lml.log`; en ese archivo buscá `trucor2_cards.ytd` o `adding`. Si no existe ese log, el archivo `scripts\TrucoRDR2.log` sigue aportando el diagnóstico del lado del juego.

Si aparece «Diccionario cargado» pero una carta queda vacía, comprobá que las texturas internas se llamen exactamente `01-bastos`, `01-copas`, `01-espadas`, `01-oros`, etc. y revisá la tabla de nombres de la herramienta antes de generar el YTD.

## Partida de truco mano a mano

La mesa permite disputar partidas completas entre el jugador y un rival controlado por la IA.

### Configuración

En `scripts/TrucoRDR2.ini`:

```ini
[Game]
; 15 o 30 puntos.
TargetPoints=30
; Flor es opcional.
FlorEnabled=false

[Audio]
Enabled=true
```

- `TargetPoints=15`: partida corta a 15.
- `TargetPoints=30`: partida tradicional a 30, con malas y buenas.
- `FlorEnabled=true`: activa la flor. Si solo un jugador tiene flor, se cuenta automáticamente; si ambos tienen, se abre la respuesta y las subidas.
- `Enabled=true`: reproduce sonidos de interfaz que ya incluye RDR2.

### Controles durante tu turno

- Flechas izquierda/derecha: seleccionar una carta.
- `Enter`: jugar la carta seleccionada.
- `E`: cantar Envido.
- `R`: cantar Real Envido.
- `F`: cantar Falta Envido.
- `T`: cantar Truco, Retruco o Vale Cuatro según la etapa de la apuesta.
- `M`: irse al mazo y conceder la mano por el valor actual de la apuesta.

### Responder a un canto

- `Q`: Quiero.
- `N`: No quiero.
- `E`, `R`, `F`: subir el envido cuando la secuencia lo permite.
- `T`: subir de Truco a Retruco o de Retruco a Vale Cuatro.
- `C`: Contra Flor.
- `B`: Contra Flor al Resto.

Las opciones que no correspondan al canto pendiente se ignoran. El envido se cierra cuando se juega la primera carta. La flor bloquea el envido cuando está habilitada y aparece en la mano.

### Desarrollo de la partida

- El tanteador se actualiza automáticamente con los puntos de envido, flor y truco.
- La mano rota entre jugador y rival.
- Se juega al mejor de tres bazas, con resolución de pardas.
- El truco vale 1 sin canto, 2 con Truco querido, 3 con Retruco querido y 4 con Vale Cuatro querido. Falta Envido aceptado usa el valor de falta de la partida, no la suma de los envidos anteriores; si se rechaza una subida, se cobran los puntos previamente establecidos a favor de quien hizo la subida.
- No querer Truco, Retruco o Vale Cuatro entrega la mano por el valor anterior de la apuesta.
- Al terminar una mano, `Enter` reparte la siguiente.
- Al terminar la partida, `Enter` comienza una partida nueva.
- `Esc` cierra la mesa.
- La combinación configurada en `[Controls]` abre/cierra la mesa. El valor predeterminado es `Ctrl+F9`; se evitan F8 (consola de ScriptHookRDR2DotNet) e Insert (recarga de scripts).

### Rival controlado por IA

El rival ya no juega cartas al azar. Evalúa su envido, la fuerza de su mano, la carta que tiene enfrente y el tamaño de la apuesta. Puede cantar o subir apuestas y aceptar/rechazar según su mano y el tanteador. Esta primera IA es deliberadamente básica: no simula todos los estilos humanos y deberá ajustarse con pruebas reales.

### Interfaz e integración con RDR2

La mesa muestra el tanteador, la etapa de malas/buenas, la mano, las bazas, las cartas jugadas y el canto pendiente. Las cartas se animan al repartirse y al jugarse. Los efectos de interfaz utilizan sonidos internos de RDR2. Mientras la mesa o su menú están abiertos, el script intenta bloquear los controles normales de movimiento y combate para que el personaje no actúe detrás de la interfaz.

La escala y la posición de la mesa siguen siendo provisionales. Se ajustarán después de las pruebas en distintas resoluciones.

## Nota sobre validación

El código está preparado para compilar contra la API V2.2.0, pero en este entorno no está instalado MSBuild/Visual Studio ni el runtime del juego, así que no puedo certificar una compilación ni probar la carga real del YTD aquí. El proyecto reporta un error de compilación explícito si no encuentra la DLL de API requerida.
