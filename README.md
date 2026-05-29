# MediaScribe Windows

MediaScribe Windows est une application Windows portable pour enregistrer le son du PC, le micro, puis transcrire l'audio localement.

Le logiciel est pensé pour un usage simple : lancer l'exécutable, choisir les sources audio, enregistrer, puis récupérer un fichier `.wav` et un fichier `.txt` de transcription. Tout fonctionne en local, sans serveur et sans API externe.

À partir de la v1.1.0, les données utilisateur sont stockées dans `%LOCALAPPDATA%\MediaScribe Recorder\`. Cela permet de garder les enregistrements, réglages, logs et historiques au même endroit même si vous changez de version du ZIP portable.

## Fonctionnalités

- Application Windows portable, sans installateur.
- Enregistrement du micro seul.
- Enregistrement du micro avec tout le son du PC.
- Enregistrement du micro avec le son d'une application ou d'une fenêtre précise, par exemple Teams, Chrome ou Edge.
- Diagnostic clair si la capture d'application ne reçoit aucun son.
- Test des entrées audio sans créer de fichier.
- Choix du micro à utiliser.
- Choix du dossier de sortie.
- Création automatique d'un dossier par enregistrement.
- Création d'un fichier audio principal `mix.wav`.
- Option pour enregistrer aussi `micro.wav` et `windows.wav`.
- Transcription locale avec `whisper.cpp`, sans API externe.
- Progression de transcription de 0% à 100%.
- Transcription automatique après enregistrement, activable ou désactivable.
- Bouton `Réessayer` pour relancer une transcription.
- Détection des transcriptions suspectes.
- Transcription avec labels `Moi:` et `Ordinateur:` quand `micro.wav` et `windows.wav` existent.
- Choix du modèle de transcription `small` ou `medium`.
- Choix de la langue de transcription, avec le français par défaut.
- Import et transcription de fichiers audio ou vidéo existants.
- Création d'un fichier `.txt` à côté du fichier audio ou vidéo transcrit.
- Boutons pour copier la transcription et ouvrir le fichier `.txt`.
- Prévisualisation audio avec lecture/pause et mini timeline.
- Historique local des dernières transcriptions.
- Logs locaux avec codes d'erreur lisibles.
- Dossiers locaux créés à côté de l'application :
  - `Tools`
  - `Models`

## Téléchargement

La dernière version est disponible ici :

https://github.com/Vayaris/MediaScribe-Windows/releases

Téléchargez uniquement le package portable complet :

```text
MediaScribeRecorder-Portable-v1.3.0.zip
```

Ce package contient tout ce qu'il faut côté application : l'exécutable, les outils de transcription, FFmpeg et les modèles Whisper. Après extraction, le fichier `MediaScribeRecorder.exe` est déjà présent dans le dossier.

## Utilisation

1. Télécharger `MediaScribeRecorder-Portable-v1.3.0.zip`.
2. Extraire le ZIP dans le dossier de votre choix.
3. Lancer `MediaScribeRecorder.exe`.
4. Choisir si le son Windows ou application doit être enregistré :
   - décocher `Enregistrer Windows / application` pour enregistrer uniquement le micro.
   - choisir `Tout le bureau` pour enregistrer tout le son du PC.
   - choisir `Application` pour enregistrer seulement une application ou une fenêtre.
5. Choisir le micro.
6. Cliquer sur `Tester les entrées audio` si besoin pour vérifier le micro et le son Windows/application.
7. Cliquer sur `Stop test` pour quitter le test audio.
8. Choisir le dossier de sortie si besoin.
9. Cliquer sur `Enregistrer`.
10. Cliquer sur `Stop`.
11. Un dossier daté est créé automatiquement dans le dossier de sortie.
12. Le fichier `mix.wav` est créé dans ce dossier.
13. La transcription démarre automatiquement si l'option est activée et si les outils et le modèle sont présents.
14. Le fichier `mix.txt` est créé à côté de `mix.wav`.

Pendant la transcription, une barre de progression indique l'avancement de `0%` à `100%`.

Si le résultat semble vide, trop court ou correspond à une phrase connue de hallucination Whisper, MediaScribe affiche un avertissement `Transcription suspecte`. Le fichier `.txt` reste créé, et le bouton `Réessayer` permet de relancer la transcription avec les réglages actuels.

Si la capture `Application` ne reçoit aucun son, MediaScribe affiche une erreur claire avec le code `REC-APP-002`. Dans ce cas, utilisez `Tout le bureau`, qui reste le mode de capture le plus fiable.

Le test audio ne crée aucun fichier. Tant qu'il est actif, les autres actions sont désactivées pour éviter de lancer un enregistrement ou une transcription par erreur.

## Importer et transcrire un fichier

Le bouton `Importer et transcrire` permet de transcrire un fichier déjà existant sans lancer d'enregistrement.

Formats pris en charge :

```text
.wav .mp3 .mp4 .m4a .aac .flac .ogg .webm .mkv .mov .avi
```

Le fichier source n'est pas modifié. MediaScribe crée un fichier `.txt` à côté du fichier importé.

## Historique local

MediaScribe garde un historique local des 50 dernières transcriptions dans `%LOCALAPPDATA%\MediaScribe Recorder\Settings\history.json`.

Depuis l'interface, il est possible de rouvrir :

- lire rapidement le fichier audio ;
- le fichier audio ou vidéo ;
- le fichier `.txt` ;
- le dossier contenant le résultat.

## Paramètres

Le bouton `Paramètres` permet de régler :

- le gain du son Windows/application ;
- le gain du micro ;
- le modèle Whisper utilisé pour la transcription.
- l'auto-transcription après enregistrement ;
- l'enregistrement optionnel des pistes séparées `micro.wav` et `windows.wav`.

Modèles disponibles dans cette version :

```text
small
medium
```

Le modèle `medium` est plus performant que `small`, mais il demande plus de ressources et peut être plus lent sur certaines machines.

Depuis la v1.3.0, `medium` est sélectionné par défaut sur une nouvelle installation. Les réglages existants ne sont pas écrasés.

## Transcription par source

Si l'option de pistes séparées est activée, MediaScribe crée :

```text
mix.wav
micro.wav
windows.wav
```

Dans ce cas, la transcription utilise les pistes séparées pour produire un texte avec labels :

```text
Moi: ...
Ordinateur: ...
```

Si cette transcription par source échoue, MediaScribe revient automatiquement à la transcription normale de `mix.wav` et affiche un avertissement.

## Organisation portable

Le dossier portable est organisé comme ceci :

```text
MediaScribeRecorder.exe
Tools/
  MediaScribeProcessLoopback.exe
  whisper-cli.exe
  ffmpeg.exe
  ffprobe.exe
  whisper.dll
  ggml*.dll
Models/
  ggml-small.bin
  ggml-medium.bin
```

Les données utilisateur sont organisées comme ceci :

```text
%LOCALAPPDATA%\MediaScribe Recorder\
Recordings/
  MediaScribe-YYYYMMDD-HHMMSS/
    mix.wav
    mix.txt
    micro.wav
    windows.wav
Logs/
Settings/
  settings.json
  history.json
```

Les enregistrements sont placés par défaut dans `%LOCALAPPDATA%\MediaScribe Recorder\Recordings`.

Les journaux d'erreur sont placés dans `%LOCALAPPDATA%\MediaScribe Recorder\Logs`.

Les réglages sont placés dans `%LOCALAPPDATA%\MediaScribe Recorder\Settings\settings.json`.

## Build depuis les sources

Prérequis :

- Windows 10 ou Windows 11 x64.
- .NET 8 SDK.
- Visual Studio 2022 Build Tools avec les outils C++.
- Windows SDK compatible avec la capture audio par processus.
- FFmpeg disponible dans le `PATH`, sauf si `ffmpeg.exe` et `ffprobe.exe` sont déjà présents dans `Tools`.

Commande de build :

```powershell
.\Build-Portable.ps1
```

Le script :

1. Compile le helper natif de capture audio par processus.
2. Télécharge les binaires Windows x64 de `whisper.cpp` si besoin.
3. Copie `ffmpeg.exe` et `ffprobe.exe`.
4. Télécharge `ggml-small.bin` et `ggml-medium.bin` si besoin.
5. Publie l'application Windows self-contained.

Sortie de build :

```text
bin\Release\net8.0-windows10.0.20348.0\win-x64\publish
```

## Différence avec MediaScribe serveur

Le projet MediaScribe original est une solution serveur pour Linux, pensée pour tourner sur une VM ou un conteneur LXC Ubuntu/Debian avec une interface web.

MediaScribe Windows est un projet séparé, pensé pour Windows, avec une interface desktop portable. Son objectif est de capturer facilement le son du PC, d'une application et du micro, puis de transcrire localement l'audio.

## Confidentialité

Tout est exécuté localement sur l'ordinateur.

Les fichiers audio et les transcriptions ne sont pas envoyés à une API externe.

## Notes de licences

Ce projet utilise notamment :

- `whisper.cpp` de ggml-org ;
- FFmpeg ;
- NAudio ;
- des APIs audio Windows ;
- du code de capture process loopback Windows inclus avec sa licence.

Avant toute redistribution modifiée, il faut vérifier les licences des dépendances concernées.
