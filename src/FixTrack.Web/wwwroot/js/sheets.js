// FixTrack · Fichas de servicio: el botón abre el diálogo de impresión del navegador (también para guardar como PDF).
document.querySelectorAll("[data-print]").forEach((button) => {
    button.addEventListener("click", () => window.print());
});
