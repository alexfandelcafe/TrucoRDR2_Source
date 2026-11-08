# TrucoRDR2 — truco argentino mano a mano

Este proyecto C# compila como `TrucoRDR2.dll` para ScriptHookRDR2DotNet V2.2.0 / .NET Framework 4.8.

**Alcance de esta build:** carga y prueba las 40 texturas y ofrece una partida mano a mano de truco argentino con tanteador, apuestas de envido y truco, flor opcional, IA heurística mejorada y un prototipo de interacción física en una única ubicación: el salón de Valentine.

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

3. Las caras de las 40 cartas deben tener estos nombres de textura, sin `.png`: `01-bastos`, `01-copas`, `01-espadas`, `01-oros`, etc. El catálogo usa rangos `01`–`07` y `10`–`12` y omite 8 y 9, que no se usan en el truco argentino. Además, incluí la textura `reverso` (sin extensión), que el mod usa para el dorso de las cartas ocultas del rival.

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
- El truco vale 1 sin canto, 2 con Truco querido, 3 con Retruco querido y 4 con Vale Cuatro querido. Falta Envido aceptado se calcula según la puntuación de la partida; no se suma como si fuera un Envido o Real Envido normal. Si se rechaza una subida, se cobran los puntos correspondientes a la apuesta previamente establecida para el jugador que lanzó la subida.
- No querer Truco, Retruco o Vale Cuatro entrega la mano por el valor anterior de la apuesta.
- Al terminar una mano, `Enter` reparte la siguiente.
- Al terminar la partida, `Enter` vuelve al menú; desde ahí podés iniciar otra.
- `Esc` cierra la mesa y, si se inició desde el salón, termina la interacción física y libera al rival temporal.
- En el salón de Valentine, `E` es la interacción principal. `Ctrl+F9` sigue disponible para abrir la interfaz directamente como acceso de desarrollo; se evitan F8 (consola de ScriptHookRDR2DotNet) e Insert (recarga de scripts).

### Interacción con el mundo (prototipo de una ubicación)

La primera ubicación está fijada al salón de Valentine. El mod detecta al jugador cerca del punto de interacción, muestra un subtítulo y permite iniciar con `E`.

- Al iniciar se guarda la posición y orientación originales del jugador.
- El mod crea una mesa (`p_table01x`) y dos sillas (`p_chair02x`) y apoya los objetos en el suelo.
- El jugador camina hasta la silla y el rival también navega desde un punto cercano; ninguno se teletransporta desde su posición inicial.
- El punto final de sentado se calcula como un desplazamiento local de cada silla (`GetOffsetPosition`): unos centímetros delante del respaldo y aproximadamente 0,50 m por encima del origen del modelo. El punto de aproximación queda más atrás, para que el personaje camine y llegue antes de sentarse.
- El jugador usa `PROP_HUMAN_SEAT_CHAIR_MP_PLAYER` y el rival `PROP_HUMAN_SEAT_CHAIR`. En `TaskInvoker.StartScenario`, el orden de los argumentos es `teleportToScenario` y luego `isSittingScenario`; ambos se invocan con `false, true`, porque los personajes ya caminaron hasta la silla y no deben teletransportarse al iniciar el escenario.
- Las orientaciones de las mallas de las sillas son independientes de las orientaciones finales de los personajes.
- Si falla la creación de un objeto o el inicio de las tareas, se cancela la preparación y se limpian los elementos temporales.
- Al cerrar con `Esc`, cancelar la preparación o descargar el script, se limpian las tareas, se elimina el rival temporal y se borran los tres objetos; el jugador se restaura si sigue vivo, fuera de combate y cerca de la mesa.
- `Ctrl+F9` sigue disponible como acceso directo de desarrollo para abrir la interfaz directamente sin desplazarse al salón y sin crear muebles.

El punto de referencia aproximado del salón es `(-307.715, 802.857, 118.979)`. El desplazamiento local de sentado (`y = -0,05`, `z = 0,50`) es una calibración inicial para el modelo `p_chair02x` y debe probarse visualmente. Los escenarios de silla están documentados en la lista de escenarios de RDR2; los nombres por sí solos no garantizan que el modelo creado por script tenga el mismo punto de asiento que una silla colocada por el juego.

### Rival controlado por IA

La IA usa heurísticas situacionales, sin consultar las cartas que el jugador todavía no jugó.

- **Selección de cartas:** intenta ganar una baza gastando la carta más barata que supera la rival; conserva cartas fuertes cuando no puede ganar, y adapta la salida al resultado de las bazas anteriores y a quién favorece una parda.
- **Apuestas de truco:** estima la fuerza relativa de las cartas restantes y ajusta la decisión de cantar, subir, aceptar o rechazar según el nivel de la apuesta y el tanteador. La evaluación se recalcula a medida que avanza la mano, por lo que también funciona con una o dos cartas restantes.
- **Envido y flor:** estima la probabilidad de ganar a partir de los puntos propios y exige más confianza ante apuestas grandes. Puede subir apuestas cuando la mano lo justifica.
- **Riesgo según tanteador:** tiende a proteger una ventaja cerca del final y a asumir más riesgo si está perdiendo.

La estrategia sigue siendo heurística, no una simulación exhaustiva ni un modelo perfecto del juego humano. Hay que validar sus decisiones y el tanteador con partidas reales.

### Interfaz y presentación

La mesa muestra el tanteador, la etapa de malas/buenas, la mano, las bazas, las cartas jugadas y el canto pendiente. Usa un panel superior oscuro con acentos dorados, paño verde y marco de madera, la textura `reverso` del YTD para las cartas ocultas y selección resaltada. Las etiquetas de cartas, VOS/RIVAL, el título "TUS CARTAS" y el estado inferior no llevan placas de fondo. El texto se dibuja con los nativos activos `UIDEBUG._BG_SET_TEXT_SCALE`, `UIDEBUG._BG_SET_TEXT_COLOR` y `UIDEBUG._BG_DISPLAY_TEXT`, que son la ruta utilizada por `TextElement` en ScriptHookRDR2DotNet V2. Se evita usar los nativos antiguos `HUD._DISPLAY_TEXT` y `HUD._SET_TEXT_COLOR`, que no tienen efecto en versiones actuales de RDR2. Se conservan la escala, los colores, la alineación y las posiciones; el dibujado de texto no mezcla llamadas de sombra del HUD con el canal `UIDEBUG`. No se fuerzan fuentes personalizadas `$title` o `$body`. Los efectos son sonidos del HUD que ya incluye RDR2.

La distribución usa un diseño de referencia 1920×1080 y se adapta con letterboxing a proporciones distintas de 16:9; el tamaño y la composición final requieren prueba dentro del juego.

## Integración y comportamiento

- La mesa desactiva los grupos de controles de jugador, cámara y frontend mientras está abierta para evitar acciones del personaje desde detrás de la interfaz.
- La partida no congela el mundo: si una caída o un ataque reduce la salud del personaje, el overlay se cierra automáticamente, pero conserva la mano y el tanteador para reanudar con Ctrl+F9.
- La combinación de apertura se lee al cargar el script desde la sección [Controls] de TrucoRDR2.ini; los cambios requieren volver a cargar/reiniciar el script.
- La escala y las posiciones están basadas en un marco de 1920×1080 que se adapta con letterboxing a proporciones distintas. Se pueden retocar durante la prueba en el juego.

## Nota sobre validación

El código está preparado para compilar contra la API V2.2.0, pero en este entorno no está instalado MSBuild/Visual Studio ni el runtime del juego, así que no puedo certificar una compilación ni probar la carga real del YTD aquí. El proyecto reporta un error de compilación explícito si no encuentra la DLL de API requerida.
