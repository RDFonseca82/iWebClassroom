const translations = {
  "pt-PT": {
    teacherConsole: "CONSOLA DO PROFESSOR", language: "Idioma", checkingServer: "A verificar servidor…", schoolAccess: "Acesso à escola",
    tokenHelp: "Introduza o token administrativo configurado no servidor. O token fica apenas nesta sessão do navegador.", adminToken: "Token administrativo", enter: "Entrar",
    classroom: "Sala", allClassrooms: "Todas as salas", refresh: "Atualizar lista", devices: "DISPOSITIVOS", computers: "Computadores", selectVisible: "Selecionar visíveis",
    actions: "AÇÕES", classControls: "Controlos da aula", studentMessage: "Mensagem aos alunos", messagePlaceholder: "Escreva uma indicação breve…", sendMessage: "Enviar mensagem",
    openApplication: "Abrir aplicação", notepad: "Bloco de Notas", calculator: "Calculadora", teacherScreen: "Ecrã do professor", startMirroring: "Iniciar espelhamento", stopMirroring: "Parar espelhamento",
    invalidToken: "Token administrativo inválido.", selectedOne: "selecionado", selectedMany: "selecionados", teacher: "Professor", student: "Aluno", noAgents: "Ainda não existem agentes nesta sala.",
    selectComputer: "Selecione pelo menos um computador.", actionSent: "Ação enviada com sucesso.", writeMessage: "Escreva uma mensagem.", selectOneTeacher: "Selecione exatamente um computador de professor.",
    serverAvailable: "Servidor disponível", serverUnavailable: "Servidor indisponível"
  },
  en: {
    teacherConsole: "TEACHER CONSOLE", language: "Language", checkingServer: "Checking server…", schoolAccess: "School access",
    tokenHelp: "Enter the administrative token configured on the server. The token is kept only for this browser session.", adminToken: "Administrative token", enter: "Sign in",
    classroom: "Classroom", allClassrooms: "All classrooms", refresh: "Refresh list", devices: "DEVICES", computers: "Computers", selectVisible: "Select visible",
    actions: "ACTIONS", classControls: "Classroom controls", studentMessage: "Message to students", messagePlaceholder: "Write a short instruction…", sendMessage: "Send message",
    openApplication: "Open application", notepad: "Notepad", calculator: "Calculator", teacherScreen: "Teacher screen", startMirroring: "Start mirroring", stopMirroring: "Stop mirroring",
    invalidToken: "Invalid administrative token.", selectedOne: "selected", selectedMany: "selected", teacher: "Teacher", student: "Student", noAgents: "There are no agents in this classroom yet.",
    selectComputer: "Select at least one computer.", actionSent: "Action sent successfully.", writeMessage: "Write a message.", selectOneTeacher: "Select exactly one teacher computer.",
    serverAvailable: "Server available", serverUnavailable: "Server unavailable"
  },
  es: {
    teacherConsole: "CONSOLA DEL PROFESOR", language: "Idioma", checkingServer: "Comprobando servidor…", schoolAccess: "Acceso al centro",
    tokenHelp: "Introduzca el token administrativo configurado en el servidor. El token se conserva solo durante esta sesión del navegador.", adminToken: "Token administrativo", enter: "Entrar",
    classroom: "Aula", allClassrooms: "Todas las aulas", refresh: "Actualizar lista", devices: "DISPOSITIVOS", computers: "Equipos", selectVisible: "Seleccionar visibles",
    actions: "ACCIONES", classControls: "Controles del aula", studentMessage: "Mensaje a los alumnos", messagePlaceholder: "Escriba una indicación breve…", sendMessage: "Enviar mensaje",
    openApplication: "Abrir aplicación", notepad: "Bloc de notas", calculator: "Calculadora", teacherScreen: "Pantalla del profesor", startMirroring: "Iniciar duplicación", stopMirroring: "Detener duplicación",
    invalidToken: "Token administrativo no válido.", selectedOne: "seleccionado", selectedMany: "seleccionados", teacher: "Profesor", student: "Alumno", noAgents: "Todavía no hay agentes en esta aula.",
    selectComputer: "Seleccione al menos un equipo.", actionSent: "Acción enviada correctamente.", writeMessage: "Escriba un mensaje.", selectOneTeacher: "Seleccione exactamente un equipo de profesor.",
    serverAvailable: "Servidor disponible", serverUnavailable: "Servidor no disponible"
  }
};
const detectedLanguage = navigator.language?.toLowerCase().startsWith("es") ? "es" : navigator.language?.toLowerCase().startsWith("en") ? "en" : "pt-PT";
const state = { token: sessionStorage.getItem("admin-token") || "", agents: [], language: localStorage.getItem("language") || detectedLanguage };
const elements = {
  authPanel: document.querySelector("#auth-panel"), workspace: document.querySelector("#workspace"),
  authForm: document.querySelector("#auth-form"), token: document.querySelector("#admin-token"),
  agents: document.querySelector("#agents"), filter: document.querySelector("#classroom-filter"),
  count: document.querySelector("#selection-count"), toast: document.querySelector("#toast")
};
function t(key) { return translations[state.language]?.[key] || translations["pt-PT"][key] || key; }
function applyLanguage() {
  document.documentElement.lang = state.language;
  document.querySelector("#language").value = state.language;
  document.querySelectorAll("[data-i18n]").forEach(element => element.textContent = t(element.dataset.i18n));
  document.querySelectorAll("[data-i18n-placeholder]").forEach(element => element.placeholder = t(element.dataset.i18nPlaceholder));
  if (state.agents.length) renderAgents(); else updateCount();
}

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { "Content-Type": "application/json", "X-Admin-Token": state.token, ...(options.headers || {}) } });
  if (response.status === 401) { logout(); throw new Error(t("invalidToken")); }
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
  elements.filter.innerHTML = `<option value="">${t("allClassrooms")}</option>` + classrooms.map(value => `<option>${escapeHtml(value)}</option>`).join("");
  elements.filter.value = classrooms.includes(current) ? current : "";
  renderAgents();
}

function renderAgents() {
  const visible = state.agents.filter(agent => !elements.filter.value || agent.classroomId === elements.filter.value);
  elements.agents.innerHTML = visible.length ? visible.map(agent => `
    <label class="agent">
      <input type="checkbox" value="${agent.agentId}" aria-label="Selecionar ${escapeHtml(agent.displayName)}">
      <span class="status-dot ${agent.isOnline ? "online" : ""}"></span>
      <span class="agent-details"><span class="agent-name">${escapeHtml(agent.displayName)}</span><span class="agent-meta">${agent.role === 1 ? t("teacher") : t("student")} · ${escapeHtml(agent.classroomId)}</span></span>
    </label>`).join("") : `<div class="empty">${t("noAgents")}</div>`;
  elements.agents.querySelectorAll("input").forEach(input => input.addEventListener("change", updateCount)); updateCount();
}

function selectedIds() { return [...elements.agents.querySelectorAll("input:checked")].map(input => input.value); }
function updateCount() { const count = selectedIds().length; elements.count.textContent = `${count} ${count === 1 ? t("selectedOne") : t("selectedMany")}`; }
function escapeHtml(value) { const node = document.createElement("span"); node.textContent = value; return node.innerHTML; }

async function send(kind, payload, targets = selectedIds()) {
  if (!targets.length) { toast(t("selectComputer")); return; }
  await api("/api/v1/commands", { method: "POST", body: JSON.stringify({ targetAgentIds: targets, kind, payload }) });
  toast(t("actionSent"));
}

elements.authForm.addEventListener("submit", async event => {
  event.preventDefault(); state.token = elements.token.value; sessionStorage.setItem("admin-token", state.token);
  try { await loadAgents(); elements.authPanel.hidden = true; elements.workspace.hidden = false; } catch (error) { toast(error.message); }
});
elements.filter.addEventListener("change", renderAgents);
document.querySelector("#language").addEventListener("change", event => { state.language = event.target.value; localStorage.setItem("language", state.language); applyLanguage(); });
document.querySelector("#refresh").addEventListener("click", () => loadAgents().catch(error => toast(error.message)));
document.querySelector("#select-all").addEventListener("click", () => { elements.agents.querySelectorAll("input").forEach(input => input.checked = true); updateCount(); });
document.querySelectorAll("[data-command]").forEach(button => button.addEventListener("click", async () => {
  try {
    if (button.dataset.command === "message") { const message = document.querySelector("#message").value.trim(); if (!message) return toast(t("writeMessage")); await send(0, { message }); }
    if (button.dataset.command === "app") await send(1, { applicationId: button.dataset.app });
    if (button.dataset.command === "share") { const teachers = selectedIds().filter(id => state.agents.find(agent => agent.agentId === id)?.role === 1); if (teachers.length !== 1) return toast(t("selectOneTeacher")); await send(2, {}, teachers); }
    if (button.dataset.command === "stop-share") { const teachers = selectedIds().filter(id => state.agents.find(agent => agent.agentId === id)?.role === 1); if (teachers.length !== 1) return toast(t("selectOneTeacher")); await send(3, {}, teachers); }
  } catch (error) { toast(error.message); }
}));

applyLanguage();
fetch("/health").then(response => { if (!response.ok) throw new Error(); document.querySelector("#server-dot").classList.add("online"); document.querySelector("#server-status").dataset.i18n = "serverAvailable"; applyLanguage(); }).catch(() => { document.querySelector("#server-status").dataset.i18n = "serverUnavailable"; applyLanguage(); });
if (state.token) loadAgents().then(() => { elements.authPanel.hidden = true; elements.workspace.hidden = false; }).catch(() => logout());
