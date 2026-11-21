// Novità di DockSpace: la pagina https://getdockstudio.it/dockspace/novita/ e il riquadro "Novità" della pagina dell'app.
// Stesso formato di web/lib/novita.js (ScreenLove).
//
// Prima di pubblicare una versione (tag dockspace-vX.Y.Z), aggiungila IN CIMA a `versioni`. Tipi delle voci:
//   nuovo · migliorato · corretto
// `inArrivo`: quello che stiamo preparando. Stato: sviluppo · programma.
// Quando una novità esce, toglila da qui e mettila nella versione.

export const versioni = [
  {
    versione: '0.1.0',
    data: '2026-11-21',
    titolo: 'Prima versione',
    voci: [
      ['nuovo', "**Mappa a riquadri**: cartelle e file in proporzione al loro peso, colorati per tipo. Clic per selezionare, doppio clic per entrare in una cartella."],
      ['nuovo', "**Scansione veloce** dei dischi NTFS: legge direttamente la tabella del disco, centinaia di migliaia di file in pochi secondi (serve avviare DockSpace come amministratore: te lo propone lui). Oppure una cartella qualsiasi, senza permessi speciali."],
      ['nuovo', "**Spazio su disco** reale accanto alla dimensione dei file: meno per i file compressi, zero per quelli di OneDrive solo online. Scegli tu quale usare per ordinare."],
      ['nuovo', "**Tipi di file** e **file più grandi**: quanto pesano video, immagini, archivi e programmi, e i 500 file più grandi della cartella scelta."],
      ['nuovo', "**Sposta nel Cestino** dopo una conferma con la dimensione: se ti sbagli, lo recuperi. Anche apri, mostra nella cartella e copia il percorso."],
      ['nuovo', "**Analizza con DockSpace** dal tasto destro su cartelle e dischi. Installer per x64 e ARM64, aggiornamenti con il controllo dell'impronta SHA-256."],
      ['nuovo', "**Aiutaci a migliorare**: statistiche anonime e segnalazioni degli errori solo se le scegli. Le cartelle che analizzi restano sul tuo computer."],
    ],
  },
];

export const inArrivo = [
  { stato: 'programma', titolo: 'Doppioni', testo: 'File identici trovati per contenuto, non solo per nome, anche se stanno in cartelle diverse.' },
  { stato: 'programma', titolo: 'Pulizia sicura', testo: 'Suggerimenti su file temporanei, cache e download vecchi: prima vedi cosa sparisce e quanto spazio recuperi, poi tutto va nel Cestino.' },
  { stato: 'programma', titolo: 'Rapporto da condividere', testo: "Un rapporto PDF, Word o HTML dell'analisi, con la mappa e le cartelle più grandi." },
  { stato: 'programma', titolo: 'DockSpace per Mac', testo: 'La stessa app anche su macOS.' },
  { stato: 'programma', titolo: 'Niente più avviso di Windows', testo: "Installer con firma digitale: al primo avvio SmartScreen non mostrerà più \"App non riconosciuta\"." },
];
