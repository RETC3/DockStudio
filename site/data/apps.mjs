// App della suite DockStudio: unica fonte per la griglia della home, le pagine /<app>/ e la pagina /novita/.
//
// stato:  disponibile (si scarica) · sviluppo (ci stiamo lavorando) · arrivo (in programma)
// novita: solo per le app in sviluppo o già uscite. { versioni, inArrivo } come in web/lib/novita.js:
//         la sotto-pagina /<app>/novita/ compare da sola quando c'è.
// funzioni: per le app non ancora uscite sono quelle previste e possono cambiare.
// logo:      icona dell'app (SVG in site/public/apps/<app>/), al posto della lettera colorata.
// copertina: immagine grande accanto al titolo nella pagina dell'app.
// scaricabile: l'app si scarica da getdockstudio.it (site/lib/downloads.js); la pagina ha la sezione "Scarica"
//            che legge /<app>/latest.json e si riempie da sola alla prima uscita.
//
// Nei testi si possono usare **grassetto** e `tasti`.

import * as screenlove from '../../web/lib/novita.js';
import * as dockspace from './novita/dockspace.mjs';
import * as dockpdf from './novita/dockpdf.mjs';

export const apps = [
  {
    slug: 'screenlove',
    nome: 'ScreenLove',
    num: '01',
    colore: '#FF3D7F',
    icona: 'S',
    logo: '/apps/screenlove/logo.svg',
    copertina: { src: '/apps/screenlove/copertina.png', larghezza: 1200, altezza: 630,
      alt: "ScreenLove: catture che si spiegano da sole. L'editor con un riquadro numerato e una freccia su una pagina catturata." },
    categoria: 'Catture dello schermo',
    breve: 'Catture di finestre, zone e pagine intere, editor con frecce e passaggi, video con audio e webcam, testo dalle immagini e documenti PDF e Word.',
    lead: "Catturi una finestra, una zona o una pagina intera, la sistemi nell'editor e la condividi in un PDF o in un documento Word. Registra anche video con audio e webcam.",
    stato: 'disponibile',
    piattaforme: ['Windows 10 e 11', 'Mac in arrivo'],
    scarica: 'https://screenlove.it/#scarica',
    sito: 'https://screenlove.it',
    funzioni: [
      ['Ogni tipo di cattura', 'Finestre, zone e schermo intero con `Stamp`, con lente di ingrandimento e selettore di colore. Con più monitor scegli quale prendere.'],
      ['Pagine intere', 'La cattura a scorrimento (`Ctrl+Maiusc+Stamp`) cuce una pagina lunga in una sola immagine, fino a 40.000 pixel.'],
      ["Editor con le maniglie", 'Frecce, forme, testo, passaggi numerati, sfocatura, spotlight e ritaglio, tutti modificabili con le maniglie.'],
      ['Video con audio e webcam', 'Registrazione MP4 (`Ctrl+Alt+R`) con audio di sistema, microfono e webcam. Poi tagli e unisci i video.'],
      ['Testo dalle immagini', 'Il testo di una zona (OCR) e i QR code copiati negli appunti, senza internet.'],
      ['PDF e Word', 'Da una o più catture nasce un documento PDF, Word o HTML, con titoli e commenti.'],
      ['Copia e salva al volo', "Storico delle catture con anteprima grande: copi negli appunti o salvi in PNG, JPG o BMP con un clic."],
      ['Sempre aggiornata', "Installer e versione portatile per x64 e ARM64. Gli aggiornamenti vengono controllati con l'impronta SHA-256."],
    ],
    novita: { versioni: screenlove.versioni, inArrivo: screenlove.inArrivo },
  },
  {
    slug: 'dockspace',
    nome: 'DockSpace',
    num: '02',
    colore: '#0F9D8A',
    icona: 'Sp',
    logo: '/apps/dockspace/logo.svg',
    copertina: { src: '/apps/dockspace/copertina.png', larghezza: 1200, altezza: 760,
      alt: 'DockSpace: a sinistra le cartelle di un disco, a destra il loro contenuto ordinato per dimensione, in basso la mappa a riquadri colorati per tipo di file.' },
    categoria: 'Spazio su disco',
    breve: 'Scopri cosa riempie il disco: una mappa a riquadri, le cartelle e i file più grandi e lo spazio che occupano davvero.',
    lead: "Una mappa del disco che mostra a colpo d'occhio cartelle e file più pesanti. Un disco intero in pochi secondi, e quello che non serve finisce nel Cestino, mai cancellato di colpo.",
    stato: 'disponibile',
    // Download dal sito (site/lib/downloads.js): la sezione "Scarica" legge /dockspace/latest.json.
    scaricabile: true,
    piattaforme: ['Windows 10 e 11', 'Mac in arrivo'],
    funzioni: [
      ['Mappa a riquadri', 'Cartelle e file disegnati in proporzione al loro peso, colorati per tipo: si vede subito dove finisce lo spazio. Doppio clic per entrare in una cartella.'],
      ['Un disco in pochi secondi', 'La scansione veloce legge direttamente la tabella dei dischi NTFS: centinaia di migliaia di file in pochi secondi. Oppure una cartella qualsiasi, senza permessi speciali.'],
      ['Lo spazio vero', "Per ogni file e cartella la dimensione e lo **spazio su disco** reale: meno per i file compressi, zero per quelli di OneDrive solo online."],
      ['Tipi e file più grandi', 'Quanto pesano video, immagini, archivi e programmi, estensione per estensione, e i 500 file più grandi della cartella scelta.'],
      ['Nel Cestino, non nel nulla', 'Quello che non serve va nel **Cestino**, dopo una conferma con la dimensione: se ti sbagli, lo recuperi.'],
      ['Dal tasto destro', 'Su una cartella o un disco, **Analizza con DockSpace** apre subito l\'analisi. Installer per x64 e ARM64, aggiornamenti con l\'impronta SHA-256.'],
    ],
    novita: { versioni: dockspace.versioni, inArrivo: dockspace.inArrivo },
  },
  {
    slug: 'dockpdf',
    nome: 'DockPDF',
    num: '03',
    colore: '#E0413A',
    icona: 'P',
    logo: '/apps/dockpdf/logo.svg',
    copertina: { src: '/apps/dockpdf/copertina.png', larghezza: 1200, altezza: 760,
      alt: 'DockPDF: la schermata iniziale con le schede degli strumenti, da Modifica il PDF e Firma fino a Comprimi e Riconosci il testo.' },
    categoria: 'PDF',
    breve: 'Modifica, firma, unisci, dividi e comprimi i PDF, e rendi cercabili le scansioni. Tutto sul tuo computer.',
    lead: "Tutti gli strumenti per i PDF in un'app sola, sul tuo computer: i documenti non passano da nessun sito. Un clic sull'icona accanto all'orologio apre il cerchio degli strumenti.",
    stato: 'disponibile',
    // Download dal sito (site/lib/downloads.js): la sezione "Scarica" legge /dockpdf/latest.json.
    scaricabile: true,
    piattaforme: ['Windows 10 e 11', 'Mac in arrivo'],
    funzioni: [
      ['Modifica davvero', 'Scrivi dove vuoi, **correggi il testo che c\'è già**, evidenzia, copri, disegna frecce e forme, aggiungi immagini. Ogni modifica si annulla.'],
      ['Firma', 'Firma **disegnata**, scritta o da immagine, salvata per la volta dopo. Oppure la **firma digitale** con il tuo certificato, anche da file .pfx.'],
      ['Moduli', 'I PDF con campi da compilare si riempiono a schermo: caselle, elenchi e testo, salvati nel file.'],
      ['Pagine in ordine', 'Unisci più PDF, riordina, gira ed elimina le pagine trascinando le miniature. Dividi per pagine, ogni N pagine o per intervalli.'],
      ['Comprimi', 'PDF più leggeri da mandare per email: scegli quanta qualità tenere, testo e disegni restano identici.'],
      ['Testo dalle scansioni', "L'OCR rende **cercabili e copiabili** i PDF scansionati, senza internet. La scansione resta com'è."],
      ['Immagini e password', 'Foto e scansioni JPG o PNG in un PDF, e pagine salvate come immagini. Proteggi un PDF con una password o toglila (se la conosci).'],
      ['Il cerchio degli strumenti', "Dall'icona nell'area di notifica si apre un cerchio con tutti gli strumenti. Installer per x64 e ARM64, \"Apri con\" per i PDF, aggiornamenti con l'impronta SHA-256."],
    ],
    novita: { versioni: dockpdf.versioni, inArrivo: dockpdf.inArrivo },
  },
  {
    slug: 'dockplay',
    nome: 'DockPlay',
    num: '04',
    colore: '#8B5CF6',
    icona: 'Pl',
    categoria: 'Video e musica',
    breve: 'Video e musica in quasi ogni formato, con sottotitoli e playlist. Senza pubblicità.',
    lead: 'Un lettore per video e musica che apre quasi tutto, senza codec da installare e senza pubblicità.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Quasi ogni formato', 'MP4, MKV, AVI, MOV, MP3, FLAC e molti altri, senza pacchetti di codec.'],
      ['Sottotitoli', 'Sottotitoli inclusi nel video o da file, con dimensione e ritardo regolabili.'],
      ['Musica e playlist', 'La tua libreria musicale con album, playlist e riproduzione continua.'],
      ['Riprende da dove eri', 'Ogni video ricorda il punto in cui ti eri fermato.'],
    ],
  },
  {
    slug: 'dockconvert',
    nome: 'DockConvert',
    num: '05',
    colore: '#F59E0B',
    icona: 'C',
    categoria: 'Convertitore',
    breve: "Immagini, documenti, audio e video da un formato all'altro, anche tanti file insieme.",
    lead: "Trascini i file, scegli il formato e li converti tutti insieme, senza caricarli su siti sconosciuti.",
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Immagini', 'PNG, JPG, WebP, HEIC e altri, con ridimensionamento e qualità a scelta.'],
      ['Documenti', 'Da Word e immagini a PDF, e da PDF a immagini.'],
      ['Audio e video', 'Da un formato all\'altro, oppure solo l\'audio di un video.'],
      ['Tanti file insieme', 'Cartelle intere in un colpo, con le impostazioni salvate per la volta dopo.'],
    ],
  },
  {
    slug: 'docknotes',
    nome: 'DockNotes',
    num: '06',
    colore: '#10B981',
    icona: 'N',
    categoria: 'Note',
    breve: 'Quaderni, sezioni e pagine libere, con immagini, catture e liste di cose da fare.',
    lead: 'Quaderni con sezioni e pagine dove scrivere, incollare immagini e catture e spuntare le cose da fare. Le note restano sul tuo computer.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Quaderni e sezioni', 'Organizzi le note come in un raccoglitore: quaderni, sezioni e pagine.'],
      ['Pagine libere', 'Testo, immagini, tabelle e liste di cose da fare nella stessa pagina.'],
      ['Catture in un clic', 'Una cattura di ScreenLove finisce direttamente nella pagina aperta.'],
      ['Ricerca', 'Trovi una parola in tutti i quaderni, anche dentro le immagini.'],
    ],
  },
  {
    slug: 'dockpost',
    nome: 'DockPost',
    num: '07',
    colore: '#2447F5',
    icona: 'Po',
    categoria: 'Posta e calendario',
    breve: "Tutte le caselle di posta in un'unica app, con calendario e contatti.",
    lead: "Le tue caselle di posta in un'unica app, con calendario e rubrica. Funziona con qualsiasi casella IMAP.",
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Tutte le caselle', 'Più indirizzi nella stessa app, ognuno con le sue cartelle, oppure tutti insieme.'],
      ['Calendario', 'Appuntamenti e promemoria accanto alla posta.'],
      ['Contatti', 'Una rubrica unica per tutte le caselle.'],
      ['La posta resta tua', 'I messaggi sono salvati sul computer e si leggono anche senza connessione.'],
    ],
  },
  {
    slug: 'dockvoice',
    nome: 'DockVoice',
    num: '08',
    colore: '#EC4899',
    icona: 'V',
    categoria: 'Registratore',
    breve: 'Registra riunioni e note vocali e trasformale in testo, senza mandarle su internet.',
    lead: 'Registri riunioni, lezioni e note vocali e le trasformi in testo direttamente sul computer: l\'audio non va su nessun server.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Registra', 'Dal microfono o dall\'audio del computer, per esempio una videochiamata.'],
      ['Trascrivi offline', 'Il parlato diventa testo sul tuo computer, senza internet.'],
      ['Cerca nelle registrazioni', 'Trovi una parola e salti al punto in cui è stata detta.'],
      ['Esporta', 'Il testo in Word o PDF, l\'audio in MP3.'],
    ],
  },
  {
    slug: 'dockframe',
    nome: 'DockFrame',
    num: '09',
    colore: '#14B8A6',
    icona: 'F',
    categoria: 'Foto',
    breve: 'Sfoglia, ordina e ritocca le foto, con album e ricerca per data e luogo.',
    lead: 'Le foto del computer sfogliate e ordinate in album, con ritocchi veloci e ricerca per data e luogo.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Sfoglia veloce', 'Migliaia di foto senza attese, organizzate per data.'],
      ['Album', 'Album e cartelle senza spostare né copiare i file.'],
      ['Ritocchi', 'Ritaglio, raddrizzamento, luce e colore, sempre annullabili.'],
      ['Data e luogo', 'Ricerca per giorno, mese o posto in cui è stata scattata la foto.'],
    ],
  },
  {
    slug: 'docktask',
    nome: 'DockTask',
    num: '10',
    colore: '#64748B',
    icona: 'T',
    categoria: 'Attività',
    breve: 'Liste, scadenze e promemoria per organizzare il lavoro e i progetti.',
    lead: 'Liste, progetti e scadenze per organizzare il lavoro, con promemoria che arrivano al momento giusto.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Liste e progetti', 'Attività raggruppate per progetto, con sotto-attività e note.'],
      ['Scadenze e promemoria', 'Avvisi sul computer quando un\'attività sta per scadere.'],
      ['Bacheca', 'Le attività come schede da spostare tra "da fare", "in corso" e "fatto".'],
      ['Oggi', 'Una vista con quello che c\'è da fare oggi, da tutti i progetti.'],
    ],
  },
  {
    slug: 'docksafe',
    nome: 'DockSafe',
    num: '11',
    colore: '#0F766E',
    icona: 'B',
    categoria: 'Backup',
    breve: 'Copie di sicurezza e sincronizzazione delle cartelle, in automatico.',
    lead: 'Copie di sicurezza delle cartelle importanti su un disco esterno o in rete, in automatico, con le versioni precedenti dei file.',
    stato: 'arrivo',
    piattaforme: ['Windows', 'Mac'],
    funzioni: [
      ['Backup automatici', 'Scegli le cartelle e ogni quanto copiarle: al resto pensa DockSafe.'],
      ['Versioni precedenti', 'Recuperi un file com\'era ieri o la settimana scorsa.'],
      ['Sincronizzazione', 'Due cartelle tenute uguali, per esempio tra il computer e un disco di rete.'],
      ['Controllo', 'Ti avvisa se un backup non è andato a buon fine.'],
    ],
  },
];

// Lavori che riguardano tutta la suite, mostrati in cima a /novita/.
export const suite = {
  inArrivo: [
    { stato: 'sviluppo', titolo: 'Un nucleo comune per tutte le app', testo: 'Aggiornamenti, licenza unica, lingue, editor delle immagini e documenti PDF e Word scritti una volta sola e condivisi da tutte le app. ScreenLove lo userà dalla prossima versione.' },
    { stato: 'programma', titolo: 'Le app anche su Mac', testo: 'Ogni app uscirà per Windows e per Mac, con le stesse funzioni e gli aggiornamenti insieme. Si parte da ScreenLove.' },
    { stato: 'programma', titolo: 'Licenza Business', testo: 'Una licenza per tutta la suite e un installer unico da distribuire sui computer dell\'ufficio, senza domande.' },
  ],
};

// Il launcher DockStudio (src/DockStudio.Launcher): non è una delle undici app, ma ha la sua pagina /dockstudio/
// e si scarica come le app da getdockstudio.it/dockstudio/.
export const launcher = {
  slug: 'dockstudio',
  nome: 'DockStudio',
  colore: '#2447F5',
  logo: '/apps/dockstudio/logo.svg',
  copertina: { src: '/apps/dockstudio/copertina.png', larghezza: 1200, altezza: 760,
    alt: 'DockStudio: la finestra "Le tue app" con ScreenLove, DockSpace e DockPDF installate e le altre app in arrivo.' },
  lead: "Tutte le app DockStudio in un posto solo: le apri, installi quelle che ti mancano con un clic e le aggiorni tutte insieme. È facoltativa: ogni app funziona anche da sola.",
  funzioni: [
    ['Le tue app', 'Le app installate in una finestra sola, con la versione di ognuna: un clic e si aprono.'],
    ['Installa con un clic', "Le app che ti mancano si scaricano dal sito ufficiale, con il **controllo dell'impronta**, e si installano senza domande."],
    ['Aggiorna tutto', 'Controlla le nuove versioni di tutte le app e le aggiorna una dopo l\'altra con **Aggiorna tutte**.'],
    ['Una sola icona', "Accanto all'orologio c'è solo DockStudio: da lì apri le app e il **cerchio degli strumenti** di DockPDF."],
    ['Le prossime app', "Le app in arrivo compaiono già nell'elenco: quando escono, le installi da qui senza aggiornare DockStudio."],
    ['Licenza della suite', 'Una licenza vale per tutte le app. Avvio con Windows, aspetto chiaro o scuro, statistiche solo se le scegli.'],
  ],
};
