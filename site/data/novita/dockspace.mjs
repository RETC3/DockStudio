// Novità di DockSpace: la pagina https://getdockstudio.it/dockspace/novita/ e il riquadro "Novità" della pagina dell'app.
// Stesso formato di web/lib/novita.js (ScreenLove).
//
// Prima di pubblicare una versione (tag dockspace-vX.Y.Z), aggiungila IN CIMA a `versioni`. Tipi delle voci:
//   nuovo · migliorato · corretto
// `inArrivo`: quello che stiamo preparando. Stato: sviluppo · programma.
// Quando una novità esce, toglila da qui e mettila nella versione.

export const versioni = [];

export const inArrivo = [
  { stato: 'sviluppo', titolo: 'Prima versione per Windows', testo: "Mappa a riquadri, cartelle e file più grandi, tipi di file, spazio su disco reale, scansione veloce dei dischi NTFS e Cestino. Installer per x64 e ARM64." },
  { stato: 'programma', titolo: 'Doppioni', testo: 'File identici trovati per contenuto, non solo per nome, anche se stanno in cartelle diverse.' },
  { stato: 'programma', titolo: 'Pulizia sicura', testo: 'Suggerimenti su file temporanei, cache e download vecchi: prima vedi cosa sparisce e quanto spazio recuperi, poi tutto va nel Cestino.' },
  { stato: 'programma', titolo: 'Rapporto da condividere', testo: "Un rapporto PDF, Word o HTML dell'analisi, con la mappa e le cartelle più grandi." },
  { stato: 'programma', titolo: 'DockSpace per Mac', testo: 'La stessa app anche su macOS.' },
  { stato: 'programma', titolo: 'Niente più avviso di Windows', testo: "Installer con firma digitale: al primo avvio SmartScreen non mostrerà più \"App non riconosciuta\"." },
];
