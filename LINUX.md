# Manager HDR Linux

Le port Linux accompagne mpv-AnimeJaNai-HDR. Windows conserve DirectML,
TensorRT et ses réglages d'écran. Le build Linux utilise .NET 10/Avalonia,
SkiaSharp et HarfBuzz natifs, sans DLL Windows.

## Moteurs

- **Vulkan** : Linux SDR, AMD/NVIDIA. Bibliothèque `libaji_vulkan.so`, modèles
  SPAN convertis `.ncnn.param/.bin`, RIFE 4.6 dans `rife-vulkan/rife-v4.6`.
  Les modèles sans conversion sont exclus de la liste. Le calcul GPU passe
  initialement par des images RGB24 et des transferts CPU/GPU.
- **TensorRT** : NVIDIA uniquement, avec son runtime et ses modèles ONNX RIFE.
  Le modèle Vulkan RIFE 4.6 n'est pas un modèle ONNX TensorRT.
- **DirectML**, RTX Video, AMF et fréquence automatique ne sont pas proposés
  sous Linux. Les profils ONNX intégrés ne sont pas activables en mode Vulkan :
  utiliser les profils Linux personnalisés fournis par le lecteur.

`backend=Vulkan` est sauvegardé/rechargé. Une bibliothèque manquante conserve
la lecture normale et bloque l'activation d'un profil d'inférence. La sélection
ne remplace pas les modèles d'un profil existant : les ajuster pour le moteur choisi.

## Réglages et communication

`linux_hdr_output=auto` suit les préférences Wayland. `sdr` force SDR ; `pq` suppose un écran HDR activé dans
GNOME ; le lecteur vérifie la sortie négociée. Un choix PQ ne valide pas le
HDR physique. Le Manager conserve les commentaires et options inconnues.

Le lanceur fournit `ANIMEJANAI_ROOT`, `ANIMEJANAI_DATA_DIR`,
`ANIMEJANAI_INFERENCE_DIR` et `ANIMEJANAI_IPC_PATH`. Les réglages sont rechargés
via un socket Unix privé, message `animejanai-reload-options`.
`PlatformServices.cs` est partagé avec le lecteur : banques séparées, empreintes,
verrou, sauvegardes atomiques et refus des conflits sans écraser les données.

## Validation du 12 octobre 2026

13 cas Linux ProfileRoundTrip passent : affichage automatique, RIFE ensemble,
commentaires/options, SDR/PQ, refus des fonctions Windows, sélection Vulkan,
filtrage des modèles, RIFE 4.6 et IPC Unix. Le Manager a été compilé pour
Linux et Windows ; l'exécution Windows reste à vérifier sur Windows.
Le lecteur associé a exécuté AnimeJaNai et RIFE sur RX 6500. La RTX 4050
n'est pas accessible pour une validation physique dans cette session.

Le journal et les mesures du lecteur sont dans `mpv-AnimeJaNai-HDR/docs/LINUX-INFERENCE.md`.

Les essais locaux du lecteur sont effectués avec un stream Twitch 1080p en
arrière-plan. Ils prouvent le fonctionnement et la stabilité, pas un débit
isolé ni une comparaison Windows/Linux. Les mesures restent contextualisées.

## Page Global Settings simplifiée

Une seule section Display indépendante du GPU : pic et gamut Auto, OLED décoché
par défaut. Les anciennes valeurs par GPU sont importées depuis la banque active
et leurs lignes sont conservées. Les champs communs priment ensuite pour tous les
GPU. Conversion SDR → HDR et grading Natural sont actifs quand la sortie HDR
est confirmée. Le lecteur neuf démarre en Performance sans RIFE. Les options
de rendu, d’inférence, de grading détaillé et de diagnostic sont repliées.
Les tests SettingsUi exercent les liaisons, le clavier, la sauvegarde et le rendu
en grande/petite fenêtre, sans ouvrir de fenêtre sur le bureau.

## Publication du workflow nettoyé

Le workflow HDR est le seul point de publication. Ses deux jobs Windows/Linux
valident la persistance et l’interface avant une publication commune. Le tag
`hdr-<SHA>` est immuable ; `latest-hdr-settings-0.8` reste compatible avec les
anciens téléchargements. Les autres assets ne sont pas supprimés. Les PR ne
publient rien. `player-source-ref.txt` fixe le code partagé du lecteur et le
lecteur fixe ce Manager via `manager-source-ref.txt`. Le patch inter-dépôts et
l’ancien workflow Release ont été supprimés.
