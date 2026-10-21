"use strict";
document.querySelectorAll("form[data-assignment-final]").forEach(form => {
    form.addEventListener("submit", event => {
        if (form.dataset.sending === "true") { event.preventDefault(); return; }
        form.dataset.sending = "true";
        form.querySelectorAll("button[type=submit]").forEach(button => { button.disabled = true; });
    });
});
window.addEventListener("pageshow", () => {
    document.querySelectorAll("form[data-assignment-final]").forEach(form => {
        delete form.dataset.sending;
        form.querySelectorAll("button[type=submit]").forEach(button => { button.disabled = false; });
    });
});
