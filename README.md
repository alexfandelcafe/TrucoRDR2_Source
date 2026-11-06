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

1. Usá la herramienta **RedM / RDR2**: https://redm.fivemad.com/tools/folders-to-ytd. No uses la versión FiveM (`fivemad.com`), que genera diccionarios para GTA V y no para RDR2. La versión RedM genera diccionarios RDR2 y entrega un ZIP de recurso. Extraé el ZIP y copiá solamente el archivo `.ytd` que está dentro de `stream`; para LML no necesitás `fxmanifest.lua` ni el README del recurso.

   Para esta build, el nombre debe ser `trucor2_cards.ytd` y la ruta manual es:

   `D:\Red Dead Redemption 2\lml\stream\trucor2_cards.ytd`

   Creá `lml\stream` si no existe. No lo dejes en `lml\TrucoRDR2\stream` salvo que esa carpeta tenga un `install.xml` válido que registre el contenido.

2. Abrí `scripts/TrucoRDR2.ini` y confirmá que la sección contiene:

   ```ini
   [Textures]
   TextureDictionary=trucor2_cards
   ```

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

## Nota sobre validación

El código está preparado para compilar contra la API V2.2.0, pero en este entorno no está instalado MSBuild/Visual Studio ni el runtime del juego, así que no puedo certificar una compilación ni probar la carga real del YTD aquí. El proyecto reporta un error de compilación explícito si no encuentra la DLL de API requerida.
