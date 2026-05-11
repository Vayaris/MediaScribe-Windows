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
- Choix du micro à utiliser.
- Choix du dossier de sortie.
- Création automatique d'un fichier audio `.wav`.
- Transcription locale avec `whisper.cpp`, sans API externe.
- Progression de transcription de 0% à 100%.
- Choix du modèle de transcription `small` ou `medium`.
- Choix de la langue de transcription, avec le français par défaut.
- Import et transcription de fichiers audio ou vidéo existants.
- Création d'un fichier `.txt` à côté du fichier audio ou vidéo transcrit.
- Boutons pour copier la transcription et ouvrir le fichier `.txt`.
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
MediaScribeRecorder-Portable-v1.1.2.zip
```

Ce package contient tout ce qu'il faut côté application : l'exécutable, les outils de transcription, FFmpeg et les modèles Whisper. Après extraction, le fichier `MediaScribeRecorder.exe` est déjà présent dans le dossier.

## Utilisation

1. Télécharger `MediaScribeRecorder-Portable-v1.1.2.zip`.
2. Extraire le ZIP dans le dossier de votre choix.
3. Lancer `MediaScribeRecorder.exe`.
4. Choisir si le son Windows ou application doit être enregistré :
   - décocher `Enregistrer Windows / application` pour enregistrer uniquement le micro.
   - choisir `Tout le bureau` pour enregistrer tout le son du PC.
   - choisir `Application` pour enregistrer seulement une application ou une fenêtre.
5. Choisir le micro.
6. Choisir le dossier de sortie si besoin.
7. Cliquer sur `Enregistrer`.
8. Cliquer sur `Stop`.
9. Le fichier `.wav` est créé automatiquement.
10. La transcription démarre automatiquement si les outils et le modèle sont présents.
11. Le fichier `.txt` est créé à côté du fichier audio.

Pendant la transcription, une barre de progression indique l'avancement de `0%` à `100%`.

Si la capture `Application` ne reçoit aucun son, MediaScribe affiche une erreur claire avec le code `REC-APP-002`. Dans ce cas, utilisez `Tout le bureau`, qui reste le mode de capture le plus fiable.

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

- le fichier audio ou vidéo ;
- le fichier `.txt` ;
- le dossier contenant le résultat.

## Paramètres

Le bouton `Paramètres` permet de régler :

- le gain du son Windows/application ;
- le gain du micro ;
- le modèle Whisper utilisé pour la transcription.

Modèles disponibles dans cette version :

```text
small
medium
```

Le modèle `medium` est plus performant que `small`, mais il demande plus de ressources et peut être plus lent sur certaines machines.

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
