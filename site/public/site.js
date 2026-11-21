// DockStudio: tema chiaro/scuro e indice laterale. Nessun tracciamento.
(function () {
  const root = document.documentElement;
  const button = document.getElementById("theme");
  const media = window.matchMedia("(prefers-color-scheme: dark)");

  const isDark = () => root.dataset.theme ? root.dataset.theme === "dark" : media.matches;
  const paint = () => {
    if (!button) return;
    button.querySelector(".moon").hidden = isDark();
    button.querySelector(".sun").hidden = !isDark();
  };

  if (button) {
    button.addEventListener("click", () => {
      const next = isDark() ? "light" : "dark";
      root.dataset.theme = next;
      try { localStorage.setItem("theme", next); } catch (e) { /* navigazione privata: pazienza */ }
      paint();
    });
  }
  media.addEventListener?.("change", paint);
  paint();

  // Indice laterale: evidenzia la sezione visibile.
  const links = [...document.querySelectorAll(".rail a")];
  const sections = links.map(a => document.querySelector(a.getAttribute("href"))).filter(Boolean);
  if ("IntersectionObserver" in window && sections.length) {
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        links.forEach(a => a.classList.toggle("active", a.getAttribute("href") === "#" + entry.target.id));
      });
    }, { rootMargin: "-45% 0px -50% 0px" });
    sections.forEach(s => observer.observe(s));
  }

  // Sezione "Scarica" delle app: versione, data e file dall'ultima release (/<app>/latest.json, stesso sito).
  const MESI = ["gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno", "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre"];
  const peso = (b) => b >= 1048576 ? (b / 1048576).toFixed(1).replace(".", ",") + " MB" : Math.max(1, Math.round(b / 1024)) + " KB";
  document.querySelectorAll(".download[data-latest]").forEach(async (box) => {
    let latest;
    try {
      const res = await fetch(box.dataset.latest, { headers: { Accept: "application/json" } });
      if (!res.ok) return; // nessuna versione ancora: resta il messaggio di attesa
      latest = await res.json();
    } catch (e) { return; }
    const assets = Array.isArray(latest.assets) ? latest.assets : [];
    const setup = assets.find(a => /-Setup-[\d.]+\.exe$/.test(a.name));
    if (!setup) return;
    const version = String(latest.tag_name || "").replace(/^v/, "");
    const d = new Date(latest.published_at);
    const quando = isNaN(d) ? "" : ` · ${d.getDate()} ${MESI[d.getMonth()]} ${d.getFullYear()}`;
    box.querySelector(".download-version").textContent = `Versione ${version}${quando}`;
    const button = box.querySelector(".download-setup");
    button.href = setup.browser_download_url;
    button.lastChild.textContent = `Installer per Windows (${peso(setup.size)})`;
    const list = box.querySelector(".download-files");
    for (const a of assets) {
      const li = document.createElement("li");
      const link = document.createElement("a");
      link.href = a.browser_download_url;
      link.textContent = a.name.endsWith(".zip") ? `Portatile ${/arm64/.test(a.name) ? "ARM64" : "x64"} (zip, ${peso(a.size)})` : `${a.name} (${peso(a.size)})`;
      li.append(link);
      if (a.sha256) {
        const sha = document.createElement("span");
        sha.className = "sha";
        sha.title = "Impronta SHA-256";
        sha.textContent = "SHA-256 " + a.sha256;
        li.append(sha);
      }
      list.append(li);
    }
    box.querySelector(".download-wait").hidden = true;
    box.querySelector(".download-ready").hidden = false;
  });
})();
