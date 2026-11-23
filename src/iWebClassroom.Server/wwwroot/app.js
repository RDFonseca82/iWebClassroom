const state = { token: sessionStorage.getItem("admin-token") || "", agents: [] };
const elements = {
  authPanel: document.querySelector("#auth-panel"), workspace: document.querySelector("#workspace"),
  authForm: document.querySelector("#auth-form"), token: document.querySelector("#admin-token"),
  agents: document.querySelector("#agents"), filter: document.querySelector("#classroom-filter"),
  count: document.querySelector("#selection-count"), toast: document.querySelector("#toast")
};

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { "Content-Type": "application/json", "X-Admin-Token": state.token, ...(options.headers || {}) } });
  if (response.status === 401) { logout(); throw new Error("Token administrativo inválido."); }
  if (!response.ok) { const detail = await response.text(); throw new Error(detail || `Erro ${response.status}`); }
  return response.status === 204 ? null : response.json();
}

function toast(message) {
  elements.toast.textContent = message; elements.toast.classList.add("visible");
  window.setTimeout(() => elements.toast.classList.remove("visible"), 3200);
}

function logout() { state.token = ""; sessionStorage.removeItem("admin-token"); elements.authPanel.hidden = false; elements.workspace.hidden = true; }

async function loadAgents() {
  state.agents = await api("/api/v1/agents");
  const current = elements.filter.value;
  const classrooms = [...new Set(state.agents.map(agent => agent.classroomId))].sort();
  elements.filter.innerHTML = '<option value="">Todas as salas</option>' + classrooms.map(value => `<option>${escapeHtml(value)}</option>`).join("");
  elements.filter.value = classrooms.includes(current) ? current : "";
  renderAgents();
}

function renderAgents() {
  const visible = state.agents.filter(agent => !elements.filter.value || agent.classroomId === elements.filter.value);
  elements.agents.innerHTML = visible.length ? visible.map(agent => `
    <label class="agent">
      <input type="checkbox" value="${agent.agentId}" aria-label="Selecionar ${escapeHtml(agent.displayName)}">
      <span class="status-dot ${agent.isOnline ? "online" : ""}"></span>
      <span class="agent-details"><span class="agent-name">${escapeHtml(agent.displayName)}</span><span class="agent-meta">${agent.role === 1 ? "Professor" : "Aluno"} · ${escapeHtml(agent.classroomId)}</span></span>
    </label>`).join("") : '<div class="empty">Ainda não existem agentes nesta sala.</div>';
  elements.agents.querySelectorAll("input").forEach(input => input.addEventListener("change", updateCount)); updateCount();
}

function selectedIds() { return [...elements.agents.querySelectorAll("input:checked")].map(input => input.value); }
function updateCount() { const count = selectedIds().length; elements.count.textContent = `${count} ${count === 1 ? "selecionado" : "selecionados"}`; }
function escapeHtml(value) { const node = document.createElement("span"); node.textContent = value; return node.innerHTML; }

async function send(kind, payload, targets = selectedIds()) {
  if (!targets.length) { toast("Selecione pelo menos um computador."); return; }
  await api("/api/v1/commands", { method: "POST", body: JSON.stringify({ targetAgentIds: targets, kind, payload }) });
  toast("Ação enviada com sucesso.");
}

elements.authForm.addEventListener("submit", async event => {
  event.preventDefault(); state.token = elements.token.value; sessionStorage.setItem("admin-token", state.token);
  try { await loadAgents(); elements.authPanel.hidden = true; elements.workspace.hidden = false; } catch (error) { toast(error.message); }
});
elements.filter.addEventListener("change", renderAgents);
document.querySelector("#refresh").addEventListener("click", () => loadAgents().catch(error => toast(error.message)));
document.querySelector("#select-all").addEventListener("click", () => { elements.agents.querySelectorAll("input").forEach(input => input.checked = true); updateCount(); });
document.querySelectorAll("[data-command]").forEach(button => button.addEventListener("click", async () => {
  try {
    if (button.dataset.command === "message") { const message = document.querySelector("#message").value.trim(); if (!message) return toast("Escreva uma mensagem."); await send(0, { message }); }
    if (button.dataset.command === "app") await send(1, { applicationId: button.dataset.app });
    if (button.dataset.command === "share") { const teachers = selectedIds().filter(id => state.agents.find(agent => agent.agentId === id)?.role === 1); if (teachers.length !== 1) return toast("Selecione exatamente um computador de professor."); await send(2, {}, teachers); }
    if (button.dataset.command === "stop-share") { const teachers = selectedIds().filter(id => state.agents.find(agent => agent.agentId === id)?.role === 1); if (teachers.length !== 1) return toast("Selecione exatamente um computador de professor."); await send(3, {}, teachers); }
  } catch (error) { toast(error.message); }
}));

fetch("/health").then(response => { if (!response.ok) throw new Error(); document.querySelector("#server-dot").classList.add("online"); document.querySelector("#server-status").textContent = "Servidor disponível"; }).catch(() => document.querySelector("#server-status").textContent = "Servidor indisponível");
if (state.token) loadAgents().then(() => { elements.authPanel.hidden = true; elements.workspace.hidden = false; }).catch(() => logout());
