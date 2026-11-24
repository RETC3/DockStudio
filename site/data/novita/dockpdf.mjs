// Novità di DockPDF: la pagina https://getdockstudio.it/dockpdf/novita/ e il riquadro "Novità" della pagina dell'app.
// Stesso formato di web/lib/novita.js (ScreenLove).
//
// Prima di pubblicare una versione (tag dockpdf-vX.Y.Z), aggiungila IN CIMA a `versioni`. Tipi delle voci:
//   nuovo · migliorato · corretto
// `inArrivo`: quello che stiamo preparando. Stato: sviluppo · programma.
// Quando una novità esce, toglila da qui e mettila nella versione.

export const versioni = [];

export const inArrivo = [
  { stato: 'sviluppo', titolo: 'Prima versione', testo: "Modifica, firma, unisci, dividi, comprimi, password e riconoscimento del testo, con il cerchio degli strumenti nell'area di notifica." },
  { stato: 'programma', titolo: 'Scanner', testo: 'Documenti di carta acquisiti dallo scanner o dalla foto del telefono, raddrizzati e trasformati in PDF.' },
  { stato: 'programma', titolo: 'Firma con marca temporale', testo: 'Firma digitale con data certa (marca temporale) e nel formato PAdES richiesto dagli uffici pubblici.' },
  { stato: 'programma', titolo: 'DockPDF per Mac', testo: 'La stessa app anche su macOS.' },
  { stato: 'programma', titolo: 'Niente più avviso di Windows', testo: "Installer con firma digitale: al primo avvio SmartScreen non mostrerà più \"App non riconosciuta\"." },
];
