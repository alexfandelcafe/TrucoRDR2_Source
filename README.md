# TrucoRDR2 — truco argentino mano a mano

Mod de **truco argentino mano a mano contra la IA** para RDR2 Story Mode, escrito en C# para **ScriptHookRDR2DotNet V2.2.0** y **.NET Framework 4.8**.

Incluye motor de reglas, tanteador, apuestas, rival heurístico, mesa superpuesta, controles de teclado y mando, configuración persistente, punto de control de partidas, visor de texturas y sonidos de interfaz.

## Requisitos

- Red Dead Redemption 2 para PC, en modo Historia.
- ScriptHookRDR2 de Alexander Blade.
- Community ScriptHookRDR2DotNet V2.2.0. El proyecto referencia la ScriptHookRDRNetAPI.dll instalada junto al juego.
- Visual Studio 2022 con la carga de trabajo de .NET Framework 4.8 o MSBuild y el targeting pack correspondiente.
- Lenny's Mod Loader (LML), para cargar el YTD personalizado.

No se incluye ni redistribuye ScriptHookRDRNetAPI.dll.

## Instalar las texturas

1. Generá el diccionario YTD en formato RDR2. La herramienta de conversión debe producir un YTD compatible con RedM/RDR2; no uses una salida para GTA V.
2. Copiá el archivo a:

   D:\Red Dead Redemption 2\lml\stream\trucor2_cards.ytd

   Creá lml\stream si no existe. Si tu juego está en otra carpeta, usá la ruta equivalente.
3. El YTD debe llamarse trucor2_cards.ytd y contener las texturas de las caras 01-bastos, 01-copas, 01-espadas, 01-oros, etc. El catálogo usa 01–07 y 10–12; no lleva 8 ni 9. Incluí también la textura reverso (sin extensión), que se utiliza para las cartas ocultas del rival.
4. En scripts\TrucoRDR2.ini, verificá:

   ~~~ini
   [Textures]
   TextureDictionary=trucor2_cards
   ~~~

## Compilar e instalar

### Método manual

Abrí TrucoRDR2.csproj en Visual Studio y compilá la configuración Release | x64. La propiedad RDR2Dir apunta por defecto a D:\Red Dead Redemption 2. También podés compilar desde una terminal:

~~~bat
msbuild TrucoRDR2.csproj /m /p:Configuration=Release /p:Platform=x64 /p:RDR2Dir="D:\Juegos\RDR2"
~~~

Si la DLL de ScriptHook está en otro lugar, indicá /p:ScriptHookRDRNetAPIDll="RUTA\ScriptHookRDRNetAPI.dll".

El resultado es bin\Release\TrucoRDR2.dll. Copiá esa DLL y TrucoRDR2.ini a RDR2\scripts\.

### Instalador por lote

También podés ejecutar Install.bat desde Windows. Acepta la ruta del juego como parámetro o usa D:\Red Dead Redemption 2 por defecto. Comprueba que estén RDR2.exe y ScriptHookRDRNetAPI.dll, compila e instala la DLL y el INI en scripts\.

~~~bat
Install.bat "D:\Juegos\RDR2"
~~~

El instalador no descarga ni instala ScriptHook, LML ni el YTD.

## Configuración

Las opciones iniciales están en scripts\TrucoRDR2.ini:

~~~ini
[Textures]
TextureDictionary=trucor2_cards

[Controls]
TableToggle=Control+F9

[Game]
; 15 o 30 puntos.
TargetPoints=30
; Activar o desactivar flor.
FlorEnabled=false

[Audio]
Enabled=true
~~~

- TargetPoints: duración de la partida (15 o 30).
- FlorEnabled: activa flor, contra flor y contra flor al resto.
- Audio.Enabled: sonidos de interfaz incluidos en RDR2.
- Controls.TableToggle: combinación para abrir/cerrar la mesa. Valor inicial: Control+F9.

Los cambios de duración, flor y sonido que hagas desde el menú se escriben en el INI automáticamente. La combinación de apertura se lee al cargar el script; cambiarla requiere recargar el script o reiniciar el juego.

## Controles

### Teclado

| Acción | Tecla |
|---|---|
| Abrir/cerrar la mesa | Ctrl+F9 (configurable en INI) |
| Mostrar/ocultar visor de texturas | F7 |
| Cambiar carta seleccionada | Flechas izquierda/derecha |
| Jugar carta seleccionada | Enter |
| Envido | E |
| Real Envido | R |
| Falta Envido | F |
| Truco / Retruco / Vale Cuatro | T |
| Irse al mazo | M |
| Quiero / No quiero | Q / N |
| Contra Flor / Contra Flor al Resto | C / B |
| Siguiente mano o volver al menú | Enter |
| Cerrar la mesa | Esc |

### Mando Xbox

| Acción | Control |
|---|---|
| Navegar menú | Cruceta arriba/abajo |
| Cambiar selección de carta | Cruceta izquierda/derecha |
| Jugar carta / Quiero | A |
| No quiero / cerrar mesa | B |
| Envido | X |
| Truco / Retruco / Vale Cuatro | Y |
| Real Envido | LB |
| Falta Envido | RB |
| Contra Flor | LT |
| Contra Flor al Resto | RT |
| Irse al mazo | Pulsar stick izquierdo (L3) |

En una respuesta pendiente, A equivale a Quiero y B a No quiero. En el resto de las pantallas, B cierra la mesa. Los controles del mando se consultan con el nativo de controles deshabilitados para que sigan funcionando mientras la mesa bloquea el movimiento y el combate del personaje. La asignación se basa en los controles frontend de RDR2; se debe confirmar con el mando que uses.

El visor de texturas F7 permite recorrer el catálogo con Av Pág/Re Pág y cerrarlo con Esc. El estado y el log scripts\TrucoRDR2.log ayudan a detectar si el YTD no existe, no se carga o tiene nombres internos incorrectos.

## Reglas y partida

- Mazo español de 40 cartas: se excluyen 8 y 9.
- Jerarquía del truco argentino, envido, real envido y falta envido.
- Truco, retruco y vale cuatro, con respuestas y rechazos.
- Flor opcional, contra flor y contra flor al resto.
- Partidas de 15 o 30 puntos, malas/buenas, rotación de mano, pardas y cierre de partida.
- Irse al mazo y resolución de puntos según la apuesta activa.
- Rival con heurísticas que ponderan cartas propias, tanteador y valor de las apuestas; no conoce las cartas ocultas del jugador.

Los reglamentos de truco varían según la mesa. La implementación contiene una interpretación concreta de las secuencias y del cálculo de Falta Envido; si se adopta otro reglamento local, sus casos de puntuación deben ajustarse y probarse explícitamente.

## Guardado y continuidad

- Mientras el script siga cargado, cerrar el overlay conserva la mano actual en memoria.
- Al terminar cada mano se guarda un único punto de control en scripts\TrucoRDR2.save, con tanteador, objetivo, flor, mano siguiente y estado final de la partida.
- Al reiniciar el juego, el menú ofrece CONTINUAR PARTIDA cuando hay un punto de control válido. Se vuelve a repartir una mano nueva con el tanteador y la mano restaurados.
- No se serializan las cartas ni una baza a medio jugar. Si el juego se cierra durante una mano, se reanuda desde el último cierre de mano. En una partida nueva, el primer punto de control se crea al finalizar la primera mano.
- Elegir NUEVA PARTIDA elimina el punto de control anterior.

Este archivo es un solo guardado de continuidad, no un historial ni estadísticas de partidas.

## Interfaz y relación con el mundo

La mesa es un overlay. Conserva el diseño de paño verde y madera, los acentos dorados, el reverso real reverso, las animaciones de reparto y las etiquetas sin placas de fondo. El texto vuelve a utilizar TextElement.Draw() con el ajuste manual de alineación; **no usa** _DISPLAY_TEXT, VAR_STRING ni las fuentes HUD nativas.

Mientras está abierta se bloquean los grupos de control de jugador, cámara y frontend. Si el personaje recibe daño o muere, el overlay se cierra para que el jugador recupere el control; la partida en memoria se conserva. El mundo no se congela y la mesa no crea un NPC sentado ni se ancla a una mesa física del mapa: esa integración requiere elegir ubicaciones, modelos y escenas y validarlos dentro del juego.

La distribución usa un diseño de referencia 1920×1080 y adapta posiciones y tamaños a otras resoluciones. La composición visual final debe probarse en el juego en las resoluciones y escalas de interfaz que se vayan a usar.

## Pruebas

Los tests puros de motor, IA, configuración y guardado están en tests\TrucoRDR2.Tests.csproj. En Windows con .NET SDK y el targeting pack de .NET Framework 4.8:

~~~bat
dotnet run --project tests\TrucoRDR2.Tests.csproj --configuration Release
~~~

El workflow .github/workflows/core-tests.yml ejecuta esa batería en Windows en cada push a la rama y en los pull requests hacia main. Estas pruebas no reemplazan la compilación del plugin contra tu DLL de ScriptHook ni las pruebas dentro de RDR2: el juego, ScriptHook y el YTD no se ejecutan en CI.

## Alcance de esta versión

Incluye una partida para dos jugadores locales (vos contra la IA) en modo Historia. No incluye partidas de cuatro/seis jugadores, multijugador online, estadísticas o historial, desbloqueos ni mazos alternativos, que quedan fuera del alcance actual.

## Diagnóstico y validación

El script crea scripts\TrucoRDR2.log, registra la ubicación esperada del YTD y los estados del streaming de texturas. No redistribuye archivos del juego ni de ScriptHook.

El repositorio incorpora pruebas automatizadas del motor independiente y CI para esas pruebas. **No se debe considerar certificada la compilación del mod ni su ejecución en RDR2 hasta validar en tu instalación** la DLL de ScriptHook, el YTD, los controles y la continuidad de partida.
