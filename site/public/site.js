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
})();
