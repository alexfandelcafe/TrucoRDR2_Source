# TrucoRDR2 — truco argentino mano a mano

Este proyecto C# compila como `TrucoRDR2.dll` para ScriptHookRDR2DotNet V2.2.0 / .NET Framework 4.8.

**Alcance de esta build:** carga y prueba las 40 texturas y ofrece una partida mano a mano de truco argentino con tanteador, apuestas de envido y truco, flor opcional, IA heurística mejorada y una interacción física persistente en una única ubicación: el salón de Valentine.

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

### Menú, reparto y animaciones de cartas

Al aceptar el prompt contextual `Jugar al truco`, aparece primero el menú de configuración que permite elegir 15/30 puntos, activar/desactivar flor y activar/desactivar sonidos. El reparto no comienza al abrir el menú: comienza al seleccionar `INICIAR PARTIDA` y confirmar con Enter. Después se reparten seis cartas alternadamente entre mano y repartidor. Durante unos 3,6 segundos se muestran únicamente la mesa y las cartas que van llegando; marcador, instrucciones y controles aparecen al terminar el sexto movimiento. La secuencia espera a que el YTD `trucor2_cards` esté cargado; si falta, muestra un aviso en vez de dejar elegir cartas invisibles.

Cada carta se anima desde el centro de la mesa hasta su posición: las tres cartas propias muestran su cara española y las tres del rival muestran `reverso`. En cada una de las seis entregas, el repartidor reproduce el gesto según el destino: `1to4` al dar la carta al otro asiento y `2to1` al darse la carta a sí mismo. Esto se aplica igual si reparte el jugador o el rival, usando el diccionario `mini_games@poker_mg@deal`. Los gestos usan `Upperbody | Secondary` (24), sin looping.

Antes del reparto, ambos personajes pueden mantener el idle sentado `no_cards_idle_a`. Al terminar de repartir, pasan a `hold_cards_idle_a` para representar que tienen las cartas en la mano. Esas dos poses sí usan `Looping | Upperbody | Secondary` (25): el bucle es necesario para sostener la postura mientras esperan o conservan cartas, y se reemplaza al cambiar de estado. Se usa `Ped.Task.PlayAnimation` de ScriptHookRDR2DotNet y se conserva como tarea principal `GENERIC_SEAT_CHAIR_TABLE_SCENARIO`.

Antes de reproducir cualquiera de esas animaciones, el mod espera 3 segundos desde que inicia el escenario de sentado del jugador. Durante esa espera no se ejecutan animaciones pendientes ni comienza el reparto visual de cartas; el menú de configuración puede mostrarse mientras el jugador termina de sentarse. Al cerrar la sesión se descarta cualquier animación que haya quedado pendiente.

Además de la interfaz, ahora se crean objetos 3D en el mundo: el mazo `p_cardssplit01x_val` queda sobre la mesa (offset vertical ajustado a 0.72) y se adjuntan tres cartas a `SKEL_L_Finger13` de cada jugador. Las cartas propias usan modelos nativos `p_crd_[rango]_[palo]01x_val`; el rival sostiene dorsos `p_crd_joker01x_val`. Si un modelo individual no está disponible, se intenta el objeto genérico `p_cs_pokercard01x`; el mazo tiene como alternativa `p_cards01x`. Los modelos físicos son los del mazo de póker de Valentine: sus palos se aproximan a los españoles (bastos/clubes, copas/corazones, espadas/picas, oros/diamantes), por lo que no reproducen literalmente el arte español del YTD. Las caras españolas exactas siguen en la interfaz 2D.

### Desarrollo de la partida

- El tanteador se actualiza automáticamente con los puntos de envido, flor y truco.
- La mano rota entre jugador y rival.
- Se juega al mejor de tres bazas, con resolución de pardas.
- El truco vale 1 sin canto, 2 con Truco querido, 3 con Retruco querido y 4 con Vale Cuatro querido. Falta Envido aceptado se calcula según la puntuación de la partida; no se suma como si fuera un Envido o Real Envido normal. Si se rechaza una subida, se cobran los puntos correspondientes a la apuesta previamente establecida para el jugador que lanzó la subida.
- No querer Truco, Retruco o Vale Cuatro entrega la mano por el valor anterior de la apuesta.
- Al terminar una mano, `Enter` reparte la siguiente.
- Al terminar la partida, `Enter` vuelve al menú; desde ahí podés iniciar otra.
- `Esc` cierra la mesa y restaura la posición del jugador; la mesa, las sillas y el rival permanecen en el salón hasta que se descarga el script.
- En el salón de Valentine, el prompt contextual del rival muestra la acción `E` para iniciar.

### Interacción con el mundo (escena persistente de Valentine)

La escena del salón de Valentine se prepara al inicializar el script, no al pulsar E.

- El mod crea la mesa (`p_table06x`), las dos sillas (`p_windsorchair03x`) y el rival al iniciar el script. Las sillas se colocan ahora a 1,0 m del centro de la mesa, acercándolas respecto de la posición anterior. Solo la mesa usa `PLACE_ENTITY_ON_GROUND_PROPERLY`; ese nativo puede colocar una silla sobre la colisión del tablero. El rival se crea directamente en el punto de sentado, no desplazado a un lado, y mesa/sillas se congelan tras colocarlas.
- El rival aparece sentado desde el inicio con el escenario GENERIC_SEAT_CHAIR_TABLE_SCENARIO, evitando la actividad de fumar del escenario anterior.
- Al acercarse a menos de 4,5 m del rival disponible aparece el prompt nativo `Jugar al truco` con el control de interacción de enRAGE y modo de mantener pulsado. Truco incorpora directamente la secuencia de `MakePrompt`: registra el prompt, fija la acción, crea el texto mediante `VAR_STRING` con memoria persistente, configura `SET_STANDARDIZED_HOLD_MODE`, asigna el grupo de foco del NPC y registra/oculta la acción hasta que corresponde activarla. No necesita `EnRAGECompanionsExtension` para crear ni usar el prompt; esa extensión solo se detecta opcionalmente para suprimir `Cabalga conmigo` y sus acciones sociales en el rival del truco.
- Si la extensión `EnRAGECompanionsExtension` está cargada, Truco instala mediante reflexión un prefijo Harmony sobre su filtro `IsEligibleGenericCandidate`. El filtro rechaza exclusivamente el `Ped.Handle` del rival de Truco, evitando que aparezca `Cabalga conmigo` para ese NPC. El comportamiento de reclutamiento y los prompts de Companion para los demás NPC permanecen intactos. Además, bloquea las acciones sociales de enRAGE para ese mismo objetivo para que no se superpongan con `Jugar al truco`. Si la versión de la extensión cambia y la integración no puede instalarse, Truco lo registra en `scripts\\TrucoRDR2.log`.
- El jugador no camina hasta la silla. Al aceptar el prompt, se guarda su posición original y se inicia directamente GENERIC_SEAT_CHAIR_TABLE_SCENARIO en la posición de su silla.
- El prompt no se ofrece si el jugador está muerto, montado o en combate. También se oculta si el rival muere, huye, está asustado o intimidado, ebrio, entra en combate, recibe daño, se cae, queda aturdido, está en ragdoll, arde, está siendo arrestado, lassoed/hogtied, se levanta o deja de ejecutar/sale del escenario.
- Si el rival entra en uno de esos estados durante una partida, la mesa se cierra y se restaura la posición original del jugador cuando es seguro hacerlo. No se crea un rival sustituto automáticamente.
- Al cerrar con Esc, la partida termina pero los muebles y el rival permanecen en el mundo. Al descargar/abortar el script se eliminan el prompt, los objetos y el rival.

El punto de referencia aproximado del salón es (-307.715, 802.857, 118.979). Las sillas quedan aproximadamente a 1,0 m a cada lado del centro de la mesa; el punto de sentado se calcula con `GetOffsetPosition` usando y = -0,05 y z = 0,50. El usuario confirmó visualmente que la mesa, las sillas y el escenario de sentado actuales funcionan correctamente; la secuencia de reparto y las animaciones secundarias nuevas requieren prueba dentro del juego.

### Rival controlado por IA

La IA usa heurísticas situacionales, sin consultar las cartas que el jugador todavía no jugó.

- **Selección de cartas:** intenta ganar una baza gastando la carta más barata que supera la rival; conserva cartas fuertes cuando no puede ganar, y adapta la salida al resultado de las bazas anteriores y a quién favorece una parda.
- **Apuestas de truco:** estima la fuerza relativa de las cartas restantes y ajusta la decisión de cantar, subir, aceptar o rechazar según el nivel de la apuesta y el tanteador. La evaluación se recalcula a medida que avanza la mano, por lo que también funciona con una o dos cartas restantes.
- **Envido y flor:** estima la probabilidad de ganar a partir de los puntos propios y exige más confianza ante apuestas grandes. Puede subir apuestas cuando la mano lo justifica.
- **Riesgo según tanteador:** tiende a proteger una ventaja cerca del final y a asumir más riesgo si está perdiendo.

La estrategia sigue siendo heurística, no una simulación exhaustiva ni un modelo perfecto del juego humano. Hay que validar sus decisiones y el tanteador con partidas reales.

### Interfaz y presentación

La mesa muestra el tanteador, la etapa de malas/buenas, la mano, las bazas, las cartas jugadas y el canto pendiente. El encabezado se reorganiza en niveles: marcador y objetivo arriba, y debajo fase, mano, bazas, cartas restantes del rival y valor del truco/flor. El panel superior es más compacto y usa tonos oscuros, filetes dorados envejecidos, paño verde y marco de madera; la zona central donde se depositan las cartas jugadas conserva su distribución y funcionamiento. La carta seleccionada se eleva ligeramente, aumenta de tamaño y recibe un halo dorado suave. Los controles aparecen abajo como teclas enmarcadas y solo se muestran cuando son válidos para el estado actual de `TrucoRound`: acciones del turno propio, respuestas y subidas permitidas, o Enter al terminar la mano/partida. Las etiquetas de cartas, VOS/RIVAL, el título "TUS CARTAS" y el estado inferior no llevan placas de fondo. El texto se dibuja con los nativos activos `UIDEBUG._BG_SET_TEXT_SCALE`, `UIDEBUG._BG_SET_TEXT_COLOR` y `UIDEBUG._BG_DISPLAY_TEXT`, que son la ruta utilizada por `TextElement` en ScriptHookRDR2DotNet V2. Se evita usar los nativos antiguos `HUD._DISPLAY_TEXT` y `HUD._SET_TEXT_COLOR`, que no tienen efecto en versiones actuales de RDR2. No se fuerza una fuente personalizada: el acabado western se consigue con la paleta, el marco y los detalles dorados sin depender de archivos tipográficos externos. Los efectos son sonidos del HUD que ya incluye RDR2.

La distribución usa un diseño de referencia 1920×1080 y se adapta con letterboxing a proporciones distintas de 16:9; el tamaño y la composición final requieren prueba dentro del juego.

## Integración y comportamiento

- La mesa desactiva los grupos de controles de jugador, cámara y frontend mientras está abierta para evitar acciones del personaje desde detrás de la interfaz.
- La partida no congela el mundo. Al pulsar Escape se cierra la mesa, se descarta la partida y se vuelve al menú en la próxima interacción; no se conserva la mano ni el tanteador. El jugador recupera su posición original. El rival permanece sentado en el mismo escenario y solo se limpia su tarea secundaria de animación.
- La escala y las posiciones están basadas en un marco de 1920×1080 que se adapta con letterboxing a proporciones distintas. Se pueden retocar durante la prueba en el juego.

## Nota sobre validación

El código está preparado para compilar contra la API V2.2.0, pero en este entorno no está instalado MSBuild/Visual Studio ni el runtime del juego, así que no puedo certificar una compilación ni probar la secuencia nueva de reparto dentro de RDR2. La secuencia espera la carga de `trucor2_cards.ytd`; si falta, deja la mesa visible con un mensaje y no permite elegir cartas invisibles. El proyecto reporta un error de compilación explícito si no encuentra la DLL de API requerida.
