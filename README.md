# TrucoRDR2 — primera build (prueba de texturas)

Este proyecto C# compila como `TrucoRDR2.dll` para ScriptHookRDR2DotNet V2.2.0 / .NET Framework 4.8.

**Alcance de esta build:** validar que RDR2 carga el YTD personalizado y que los nombres de textura coinciden. Incluye el catálogo de 40 cartas del truco argentino y helpers puros de jerarquía/envido. Todavía no implementa la partida completa, la mesa, los NPC ni la IA.

## Requisitos

- RDR2 para PC en modo Historia.
- ScriptHookRDR2 de Alexander Blade.
- Community ScriptHookRDR2DotNet V2.2.0 (usa la `ScriptHookRDRNetAPI.dll` de esa misma instalación).
- .NET Framework 4.8 Developer Pack / targeting pack de Visual Studio.
- Lenny's Mod Loader para que el YTD personalizado se cargue como recurso independiente.

No se incluye ni redistribuye `ScriptHookRDRNetAPI.dll`; el proyecto debe referenciar la DLL instalada con ScriptHookRDR2DotNet.

## Configurar el YTD

1. Para una instalación manual sin manifiesto, creá `lml\\stream` si no existe y colocá el archivo directamente ahí:

   `D:\Red Dead Redemption 2\lml\TrucoRDR2\stream\trucor2_cards.ytd`

   No lo dejes en `lml\\TrucoRDR2\\stream` salvo que esa carpeta de mod tenga un `install.xml` válido que registre el contenido. LML usa `lml\\stream` como carpeta de streaming manual.

2. Abrí `scripts/TrucoRDR2.ini` y confirmá que la sección contiene:

   ```ini
   [Textures]
   TextureDictionary=trucor2_cards
   ```

3. Las cartas deben tener estos nombres de textura, sin `.png`: `01-bastos`, `01-copas`, `01-espadas`, `01-oros`, etc. El catálogo usa rangos `01`–`07` y `10`–`12` y omite 8 y 9, que no se usan en el truco argentino.

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

- `F7`: mostrar u ocultar la vista de texturas. F6 queda libre porque abre el modo foto de RDR2.
- `Av Pág` (`PageDown`): avanzar una carta en el catálogo.
- `Re Pág` (`PageUp`): retroceder una carta.
- `Esc`: cerrar la vista.

Se muestran tres cartas de prueba con los nombres de textura encima. La línea de estado indica si el diccionario fue cargado por el juego.

Si el estado sigue en «Solicitando YTD», revisá que `vfs.asi` esté junto a `RDR2.exe`, que exista `lml\\stream\\trucor2_cards.ytd` y buscá en el log de LML una entrada `adding` para ese YTD. Si aparece «Diccionario cargado» pero una carta queda vacía, comprobá que el YTD sea un diccionario válido para RDR2 y que sus texturas internas se llamen exactamente `01-bastos`, `01-copas`, `01-espadas`, `01-oros`, etc.

## Nota sobre validación

El código está preparado para compilar contra la API V2.2.0, pero en este entorno no está instalado MSBuild/Visual Studio ni el runtime del juego, así que no puedo certificar una compilación ni probar la carga real del YTD aquí. El proyecto reporta un error de compilación explícito si no encuentra la DLL de API requerida.
