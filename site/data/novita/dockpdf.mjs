// Novità di DockPDF: la pagina https://getdockstudio.it/dockpdf/novita/ e il riquadro "Novità" della pagina dell'app.
// Stesso formato di web/lib/novita.js (ScreenLove).
//
// Prima di pubblicare una versione (tag dockpdf-vX.Y.Z), aggiungila IN CIMA a `versioni`. Tipi delle voci:
//   nuovo · migliorato · corretto
// `inArrivo`: quello che stiamo preparando. Stato: sviluppo · programma.
// Quando una novità esce, toglila da qui e mettila nella versione.

export const versioni = [
  {
    versione: '0.1.0',
    data: '2026-11-24',
    titolo: 'Prima versione',
    voci: [
      ['nuovo', "**Modifica il PDF**: scrivi dove vuoi, **correggi il testo che c'è già**, evidenzia, copri, disegna frecce e forme, aggiungi immagini. Ogni modifica si annulla."],
      ['nuovo', "**Firma** disegnata, scritta o da immagine, salvata per la volta dopo, oppure **firma digitale** con il tuo certificato (anche da file .pfx)."],
      ['nuovo', "**Moduli compilabili**: caselle, elenchi e campi di testo riempiti a schermo e salvati nel file."],
      ['nuovo', "**Unisci, organizza, dividi ed estrai**: pagine riordinate, girate ed eliminate trascinando le miniature; divisione per pagine, ogni N pagine o per intervalli."],
      ['nuovo', "**Comprimi** con tre livelli di qualità: si ricomprimono solo le immagini troppo grandi, testo e disegni restano identici."],
      ['nuovo', "**Riconosci il testo (OCR)**: le scansioni diventano cercabili e copiabili, senza internet, con l'OCR di Windows."],
      ['nuovo', "**Da immagini a PDF** e **da PDF a immagini**, **password** per proteggere un PDF o toglierla se la conosci."],
      ['nuovo', "**Cerchio degli strumenti** dall'icona nell'area di notifica, \"Apri con\" per i PDF e avvio con Windows. Installer per x64 e ARM64, aggiornamenti con l'impronta SHA-256."],
      ['nuovo', "**Aiutaci a migliorare**: statistiche anonime e segnalazioni degli errori solo se le scegli. I tuoi PDF restano sul tuo computer."],
    ],
  },
];

export const inArrivo = [
  { stato: 'programma', titolo: 'Scanner', testo: 'Documenti di carta acquisiti dallo scanner o dalla foto del telefono, raddrizzati e trasformati in PDF.' },
  { stato: 'programma', titolo: 'Firma con marca temporale', testo: 'Firma digitale con data certa (marca temporale) e nel formato PAdES richiesto dagli uffici pubblici.' },
  { stato: 'programma', titolo: 'DockPDF per Mac', testo: 'La stessa app anche su macOS.' },
  { stato: 'programma', titolo: 'Niente più avviso di Windows', testo: "Installer con firma digitale: al primo avvio SmartScreen non mostrerà più \"App non riconosciuta\"." },
];
