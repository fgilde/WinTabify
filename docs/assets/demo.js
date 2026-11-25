const figure = document.querySelector("[data-demo]");
const stage = figure.querySelector(".stage");
const toggle = figure.querySelector("input");
const button = figure.querySelector("button");
const state = figure.querySelector("[data-state]");
let gathered = false;

function render() {
  stage.classList.toggle("gathered", gathered);
  stage.classList.toggle("groups-out", !toggle.checked);
  state.textContent = gathered
    ? toggle.checked
      ? state.dataset.one
      : state.dataset.two
    : state.dataset.three;
  button.textContent = gathered ? button.dataset.reset : button.dataset.gather;
}
button.addEventListener("click", () => {
  gathered = !gathered;
  render();
});
toggle.addEventListener("change", render);
render();
