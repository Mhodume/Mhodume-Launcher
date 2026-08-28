# Mhodume Launcher

The one place to start VHOLUME, either way you want to play.

- **Training** starts the game with the Mhodume tools and the overlay: crosshair,
  trajectory, checkpoints, ghost inputs, the practice features. While these are
  present the game will not submit a time — that is the game's own rule, and it
  is the point: you practise freely, and nothing you do here reaches the
  leaderboard.
- **Ranked** starts the plain, unmodified game, so a run counts.

Switching either way restarts the game for you. That restart cannot be avoided:
VHOLUME marks a session ineligible whenever the mod loader is present in the
process, and the loader can only be added or removed while the game is closed.
The launcher does the whole sequence — close, set the loader, start again
through Steam — so for you it is one button.

## How it works

The launcher never touches the running game and never touches how the game
decides a run is valid. It sets one thing on disk before launch — whether the
mod loader (`dwmapi.dll`) is present — and then starts VHOLUME through Steam, so
the process carries the Steam context a submitted run needs. Ranked is the real,
untouched game; that is what makes its times count.

## Requirements

- VHOLUME installed through Steam.
- Windows. Borderless-windowed is best if you want the overlay over the game;
  exclusive fullscreen lets nothing draw on top.

---

# Mhodume Launcher (français)

Le point d'entrée unique pour lancer VHOLUME, dans le mode que tu veux.

- **Training** lance le jeu avec les outils Mhodume et l'overlay : crosshair,
  trajectoire, checkpoints, inputs du fantôme, les fonctions d'entraînement. Tant
  qu'ils sont présents, le jeu ne soumet aucun temps — c'est la règle du jeu
  lui-même, et c'est le but : tu t'entraînes librement, et rien de ce que tu
  fais ici n'atteint le leaderboard.
- **Ranked** lance le jeu nu, sans rien ajouté, pour qu'une run compte.

Passer d'un mode à l'autre relance le jeu à ta place. Ce redémarrage est
inévitable : VHOLUME invalide une session dès que le chargeur du mod est présent
dans le process, et le chargeur ne peut être ajouté ou retiré que jeu fermé. Le
launcher fait toute la séquence — fermer, régler le chargeur, relancer par
Steam — donc pour toi c'est un seul bouton.

## Comment ça marche

Le launcher ne touche jamais au jeu en cours d'exécution, ni à la façon dont le
jeu décide qu'une run est valide. Il règle une seule chose sur le disque avant
le lancement — la présence ou non du chargeur (`dwmapi.dll`) — puis démarre
VHOLUME par Steam, pour que le process porte le contexte Steam dont une run
soumise a besoin. Ranked, c'est le vrai jeu intact ; c'est ce qui fait compter
ses temps.

## Prérequis

- VHOLUME installé via Steam.
- Windows. Le fenêtré sans bordure est préférable si tu veux l'overlay par
  dessus le jeu ; le plein écran exclusif ne laisse rien s'afficher au-dessus.
