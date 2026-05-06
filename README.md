# MediaScribe Windows

MediaScribe Windows est une application Windows portable pour enregistrer le son du PC, le micro, puis transcrire l'audio localement.

Le logiciel est pensé pour un usage simple : lancer l'exécutable, choisir les sources audio, enregistrer, puis récupérer un fichier `.wav` et un fichier `.txt` de transcription. Tout fonctionne en local, sans serveur et sans API externe.

## Fonctionnalités

- Application Windows portable, sans installateur.
- Enregistrement du micro seul.
- Enregistrement du micro avec tout le son Windows.
- Enregistrement du micro avec le son d'une application ou d'une fenêtre précise, par exemple Teams, Chrome ou Edge.
- Capture d'application basée sur l'audio loopback par processus Windows.
- Vumètres séparés pour le son Windows/application et le micro.
- Animation de vumètre lissée pour éviter les variations trop brusques.
- Réglage du gain du micro.
- Réglage du gain du son Windows/application.
- Transcription locale avec `whisper.cpp`.
- Choix du modèle de transcription :
  - `small` : plus rapide et plus léger.
  - `medium` : plus précis, mais plus lent et plus lourd.
- Langue française sélectionnée par défaut.
- Import et transcription de fichiers audio ou vidéo existants.
- Panneau de transcription avec copie du texte et ouverture du fichier `.txt`.
- Dossiers locaux créés à côté de l'application :
  - `Recordings`
  - `Logs`
  - `Settings`
  - `Tools`
  - `Models`

## Téléchargement

La dernière version est disponible ici :

https://github.com/Vayaris/MediaScribe-Windows/releases

Pour une utilisation complète, il faut télécharger le package portable :

```text
MediaScribeRecorder-Portable-v1.0.0.zip
```

Ce package contient l'application, les outils de transcription, FFmpeg et les modèles Whisper.

La release contient aussi :

```text
MediaScribeRecorder.exe
```

Cet exécutable direct permet de lancer l'application, mais pour profiter de la transcription locale sans rien configurer, le ZIP portable complet est recommandé.

## Utilisation

1. Télécharger `MediaScribeRecorder-Portable-v1.0.0.zip`.
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

## Importer et transcrire un fichier

Le bouton `Importer et transcrire` permet de transcrire un fichier déjà existant sans lancer d'enregistrement.

Formats pris en charge :

```text
.wav .mp3 .mp4 .m4a .aac .flac .ogg .webm .mkv .mov .avi
```

Le fichier source n'est pas modifié. MediaScribe crée un fichier `.txt` à côté du fichier importé.

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
Recordings/
Logs/
Settings/
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

Les enregistrements sont placés par défaut dans `Recordings`.

Les journaux d'erreur sont placés dans `Logs`.

Les réglages sont placés dans `Settings/settings.json`.

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
