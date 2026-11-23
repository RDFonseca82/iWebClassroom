const translations = {
  "pt-PT": {
    teacherConsole: "CONSOLA DO PROFESSOR", language: "Idioma", checkingServer: "A verificar servidor…", schoolAccess: "Acesso do professor",
    tokenHelp: "Introduza o PIN de professor. O PIN fica apenas nesta sessão do navegador.", teacherPin: "PIN de professor", enter: "Entrar",
    classroom: "Sala", teacherWorkstation: "Posto do professor", duration: "Duração", startLesson: "Iniciar aula", endLesson: "Terminar aula", refresh: "Atualizar lista",
    devices: "DISPOSITIVOS", computers: "Computadores da sala", selectVisible: "Selecionar disponíveis", actions: "AÇÕES", classControls: "Controlos da aula",
    studentMessage: "Mensagem aos alunos", messagePlaceholder: "Escreva uma indicação breve…", sendMessage: "Enviar mensagem", openApplication: "Abrir aplicação",
    notepad: "Bloco de Notas", calculator: "Calculadora", teacherScreen: "Ecrã do professor", startMirroring: "Iniciar espelhamento", stopMirroring: "Parar espelhamento",
    invalidPin: "PIN de professor inválido.", selectedOne: "selecionado", selectedMany: "selecionados", student: "Aluno", online: "online", offline: "offline",
    noRooms: "Ainda não existem salas configuradas.", noTeachers: "Não existem postos de professor configurados.", noAgents: "Ainda não existem computadores de aluno nesta sala.",
    selectComputer: "Selecione pelo menos um computador online.", actionSent: "Ação enviada com sucesso.", writeMessage: "Escreva uma mensagem.", selectTeacher: "Selecione um posto de professor.",
    startFirst: "Inicie a aula para controlar os computadores desta sala.", lessonReady: "Sala pronta. Selecione um posto de professor e inicie a aula.",
    lessonActive: "Aula ativa em {room} · {online}/{total} computadores online · termina às {time}", lessonStarted: "A aula foi iniciada e os computadores foram associados automaticamente.",
    lessonEnded: "A aula terminou.", confirmEnd: "Terminar esta aula e desligar os computadores da sessão?", serverAvailable: "Servidor disponível", serverUnavailable: "Servidor indisponível"
  },
  en: {
    teacherConsole: "TEACHER CONSOLE", language: "Language", checkingServer: "Checking server…", schoolAccess: "Teacher access",
    tokenHelp: "Enter the teacher PIN. The PIN is kept only for this browser session.", teacherPin: "Teacher PIN", enter: "Sign in",
    classroom: "Room", teacherWorkstation: "Teacher workstation", duration: "Duration", startLesson: "Start lesson", endLesson: "End lesson", refresh: "Refresh list",
    devices: "DEVICES", computers: "Room computers", selectVisible: "Select available", actions: "ACTIONS", classControls: "Lesson controls",
    studentMessage: "Message to students", messagePlaceholder: "Write a short instruction…", sendMessage: "Send message", openApplication: "Open application",
    notepad: "Notepad", calculator: "Calculator", teacherScreen: "Teacher screen", startMirroring: "Start mirroring", stopMirroring: "Stop mirroring",
    invalidPin: "Invalid teacher PIN.", selectedOne: "selected", selectedMany: "selected", student: "Student", online: "online", offline: "offline",
    noRooms: "There are no configured rooms yet.", noTeachers: "There are no configured teacher workstations.", noAgents: "There are no student computers in this room yet.",
    selectComputer: "Select at least one online computer.", actionSent: "Action sent successfully.", writeMessage: "Write a message.", selectTeacher: "Select a teacher workstation.",
    startFirst: "Start the lesson to control the computers in this room.", lessonReady: "Room ready. Select a teacher workstation and start the lesson.",
    lessonActive: "Lesson active in {room} · {online}/{total} computers online · ends at {time}", lessonStarted: "The lesson started and the computers were added automatically.",
    lessonEnded: "The lesson has ended.", confirmEnd: "End this lesson and disconnect its computers?", serverAvailable: "Server available", serverUnavailable: "Server unavailable"
  },
  es: {
    teacherConsole: "CONSOLA DEL PROFESOR", language: "Idioma", checkingServer: "Comprobando servidor…", schoolAccess: "Acceso del profesor",
    tokenHelp: "Introduzca el PIN de profesor. El PIN se conserva solo durante esta sesión del navegador.", teacherPin: "PIN de profesor", enter: "Entrar",
    classroom: "Aula", teacherWorkstation: "Puesto del profesor", duration: "Duración", startLesson: "Iniciar clase", endLesson: "Terminar clase", refresh: "Actualizar lista",
    devices: "DISPOSITIVOS", computers: "Equipos del aula", selectVisible: "Seleccionar disponibles", actions: "ACCIONES", classControls: "Controles de la clase",
    studentMessage: "Mensaje a los alumnos", messagePlaceholder: "Escriba una indicación breve…", sendMessage: "Enviar mensaje", openApplication: "Abrir aplicación",
    notepad: "Bloc de notas", calculator: "Calculadora", teacherScreen: "Pantalla del profesor", startMirroring: "Iniciar duplicación", stopMirroring: "Detener duplicación",
    invalidPin: "PIN de profesor no válido.", selectedOne: "seleccionado", selectedMany: "seleccionados", student: "Alumno", online: "conectado", offline: "desconectado",
    noRooms: "Todavía no hay aulas configuradas.", noTeachers: "No hay puestos de profesor configurados.", noAgents: "Todavía no hay equipos de alumnos en esta aula.",
    selectComputer: "Seleccione al menos un equipo conectado.", actionSent: "Acción enviada correctamente.", writeMessage: "Escriba un mensaje.", selectTeacher: "Seleccione un puesto de profesor.",
    startFirst: "Inicie la clase para controlar los equipos de esta aula.", lessonReady: "Aula preparada. Seleccione un puesto de profesor e inicie la clase.",
    lessonActive: "Clase activa en {room} · {online}/{total} equipos conectados · termina a las {time}", lessonStarted: "La clase se inició y los equipos se añadieron automáticamente.",
    lessonEnded: "La clase ha terminado.", confirmEnd: "¿Terminar esta clase y desconectar sus equipos?", serverAvailable: "Servidor disponible", serverUnavailable: "Servidor no disponible"
  }
};

const detectedLanguage = navigator.language?.toLowerCase().startsWith("es") ? "es" : navigator.language?.toLowerCase().startsWith("en") ? "en" : "pt-PT";
const state = {
  pin: sessionStorage.getItem("teacher-pin") || "", agents: [], lessons: [], selected: new Set(), renderedLessonId: null,
  language: localStorage.getItem("language") || detectedLanguage
};
const elements = {
  authPanel: document.querySelector("#auth-panel"), workspace: document.querySelector("#workspace"), authForm: document.querySelector("#auth-form"),
  pin: document.querySelector("#teacher-pin"), agents: document.querySelector("#agents"), room: document.querySelector("#room"), teacher: document.querySelector("#teacher"),
  duration: document.querySelector("#duration"), start: document.querySelector("#start-lesson"), end: document.querySelector("#end-lesson"),
  lessonStatus: document.querySelector("#lesson-status"), count: document.querySelector("#selection-count"), toast: document.querySelector("#toast")
};

function t(key, values = {}) {
  let value = translations[state.language]?.[key] || translations["pt-PT"][key] || key;
  Object.entries(values).forEach(([name, replacement]) => { value = value.replace(`{${name}}`, replacement); });
  return value;
}

function applyLanguage() {
  document.documentElement.lang = state.language;
  document.querySelector("#language").value = state.language;
  document.querySelectorAll("[data-i18n]").forEach(element => element.textContent = t(element.dataset.i18n));
  document.querySelectorAll("[data-i18n-placeholder]").forEach(element => element.placeholder = t(element.dataset.i18nPlaceholder));
  if (state.agents.length) renderOverview(); else updateCount();
}

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { "Content-Type": "application/json", "Accept-Language": state.language, "X-Teacher-Pin": state.pin, ...(options.headers || {}) } });
  if (response.status === 401) { logout(); throw new Error(t("invalidPin")); }
  if (!response.ok) {
    const body = await response.text();
    let detail = body;
    try { const parsed = JSON.parse(body); detail = parsed.error || parsed.detail || body; } catch { /* plain response */ }
    throw new Error(detail || `Erro ${response.status}`);
  }
  return response.status === 204 ? null : response.json();
}

function toast(message) {
  elements.toast.textContent = message; elements.toast.classList.add("visible");
  window.setTimeout(() => elements.toast.classList.remove("visible"), 3400);
}

function logout() { state.pin = ""; sessionStorage.removeItem("teacher-pin"); elements.authPanel.hidden = false; elements.workspace.hidden = true; }
function currentLesson() { return state.lessons.find(lesson => lesson.roomId === elements.room.value); }
function selectedIds() { return [...elements.agents.querySelectorAll("input:checked")].map(input => input.value); }
function escapeHtml(value) { const node = document.createElement("span"); node.textContent = value ?? ""; return node.innerHTML; }

async function loadOverview() {
  const overview = await api("/api/v1/teacher/overview");
  state.agents = overview.agents || [];
  state.lessons = overview.lessons || [];
  renderOverview();
}

function renderOverview() {
  const previousRoom = elements.room.value;
  const rooms = [...new Set(state.agents.filter(agent => agent.role === 0).map(agent => agent.classroomId))].sort((a, b) => a.localeCompare(b));
  elements.room.innerHTML = rooms.length ? rooms.map(room => `<option value="${escapeHtml(room)}">${escapeHtml(room)}</option>`).join("") : `<option value="">${t("noRooms")}</option>`;
  elements.room.value = rooms.includes(previousRoom) ? previousRoom : (rooms[0] || "");

  const teachers = state.agents.filter(agent => agent.role === 1).sort((a, b) => a.displayName.localeCompare(b.displayName));
  elements.teacher.innerHTML = teachers.length
    ? teachers.map(agent => `<option value="${agent.agentId}">${escapeHtml(agent.displayName)} · ${escapeHtml(agent.classroomId)} · ${t(agent.isOnline ? "online" : "offline")}</option>`).join("")
    : `<option value="">${t("noTeachers")}</option>`;

  const lesson = currentLesson();
  if (lesson) elements.teacher.value = lesson.teacherAgentId;
  elements.teacher.disabled = Boolean(lesson);
  elements.duration.disabled = Boolean(lesson);
  elements.start.hidden = Boolean(lesson);
  elements.end.hidden = !lesson;
  elements.start.disabled = !rooms.length || !teachers.length;
  setActionsEnabled(Boolean(lesson));
  renderLessonStatus(lesson);
  renderAgents(lesson);
}

function renderLessonStatus(lesson) {
  if (!lesson) {
    elements.lessonStatus.className = "lesson-status card ready";
    elements.lessonStatus.textContent = state.agents.length ? t("lessonReady") : t("noRooms");
    return;
  }
  const students = state.agents.filter(agent => lesson.memberAgentIds.includes(agent.agentId) && agent.role === 0);
  const online = students.filter(agent => agent.isOnline).length;
  const time = new Intl.DateTimeFormat(state.language, { hour: "2-digit", minute: "2-digit" }).format(new Date(lesson.expiresAtUtc));
  elements.lessonStatus.className = "lesson-status card active";
  elements.lessonStatus.textContent = t("lessonActive", { room: lesson.roomId, online, total: students.length, time });
}

function renderAgents(lesson) {
  const students = state.agents.filter(agent => agent.role === 0 && agent.classroomId === elements.room.value);
  if (lesson?.lessonId !== state.renderedLessonId) {
    state.selected = new Set(students.filter(agent => agent.isOnline && lesson?.memberAgentIds.includes(agent.agentId)).map(agent => agent.agentId));
    state.renderedLessonId = lesson?.lessonId || null;
  }
  const availableIds = new Set(students.filter(agent => agent.isOnline && lesson?.memberAgentIds.includes(agent.agentId)).map(agent => agent.agentId));
  state.selected = new Set([...state.selected].filter(id => availableIds.has(id)));
  elements.agents.innerHTML = students.length ? students.map(agent => {
    const available = Boolean(lesson?.memberAgentIds.includes(agent.agentId)) && agent.isOnline;
    return `<label class="agent ${available ? "available" : ""}">
      <input type="checkbox" value="${agent.agentId}" ${state.selected.has(agent.agentId) ? "checked" : ""} ${available ? "" : "disabled"} aria-label="${escapeHtml(agent.displayName)}">
      <span class="status-dot ${agent.isOnline ? "online" : ""}"></span>
      <span class="agent-details"><span class="agent-name">${escapeHtml(agent.displayName)}</span><span class="agent-meta">${t("student")} · ${t(agent.isOnline ? "online" : "offline")}</span></span>
    </label>`;
  }).join("") : `<div class="empty">${t("noAgents")}</div>`;
  elements.agents.querySelectorAll("input").forEach(input => input.addEventListener("change", () => {
    if (input.checked) state.selected.add(input.value); else state.selected.delete(input.value);
    updateCount();
  }));
  updateCount();
}

function updateCount() { const count = selectedIds().length; elements.count.textContent = `${count} ${count === 1 ? t("selectedOne") : t("selectedMany")}`; }
function setActionsEnabled(enabled) {
  document.querySelectorAll("#lesson-actions button, #lesson-actions textarea").forEach(element => { element.disabled = !enabled; });
  document.querySelector("#lesson-actions").classList.toggle("disabled", !enabled);
}

async function send(kind, payload, targets = selectedIds()) {
  const lesson = currentLesson();
  if (!lesson) { toast(t("startFirst")); return; }
  if (!targets.length) { toast(t("selectComputer")); return; }
  await api(`/api/v1/lessons/${lesson.lessonId}/commands`, { method: "POST", body: JSON.stringify({ targetAgentIds: targets, kind, payload }) });
  toast(t("actionSent"));
}

elements.authForm.addEventListener("submit", async event => {
  event.preventDefault(); state.pin = elements.pin.value.trim(); sessionStorage.setItem("teacher-pin", state.pin);
  try { await loadOverview(); elements.authPanel.hidden = true; elements.workspace.hidden = false; } catch (error) { toast(error.message); }
});

elements.start.addEventListener("click", async () => {
  if (!elements.teacher.value) return toast(t("selectTeacher"));
  try {
    await api("/api/v1/lessons", { method: "POST", body: JSON.stringify({ roomId: elements.room.value, teacherAgentId: elements.teacher.value, durationMinutes: Number(elements.duration.value) }) });
    await loadOverview(); toast(t("lessonStarted"));
  } catch (error) { toast(error.message); }
});

elements.end.addEventListener("click", async () => {
  const lesson = currentLesson();
  if (!lesson || !window.confirm(t("confirmEnd"))) return;
  try { await api(`/api/v1/lessons/${lesson.lessonId}`, { method: "DELETE" }); await loadOverview(); toast(t("lessonEnded")); } catch (error) { toast(error.message); }
});

elements.room.addEventListener("change", renderOverview);
document.querySelector("#language").addEventListener("change", event => { state.language = event.target.value; localStorage.setItem("language", state.language); applyLanguage(); });
document.querySelector("#refresh").addEventListener("click", () => loadOverview().catch(error => toast(error.message)));
document.querySelector("#select-all").addEventListener("click", () => {
  elements.agents.querySelectorAll("input:not(:disabled)").forEach(input => { input.checked = true; state.selected.add(input.value); });
  updateCount();
});
document.querySelectorAll("[data-command]").forEach(button => button.addEventListener("click", async () => {
  try {
    if (button.dataset.command === "message") { const message = document.querySelector("#message").value.trim(); if (!message) return toast(t("writeMessage")); await send(0, { message }); }
    if (button.dataset.command === "app") await send(1, { applicationId: button.dataset.app });
    if (button.dataset.command === "share") { const lesson = currentLesson(); if (!lesson) return toast(t("startFirst")); await send(2, {}, [lesson.teacherAgentId]); }
    if (button.dataset.command === "stop-share") { const lesson = currentLesson(); if (!lesson) return toast(t("startFirst")); await send(3, {}, [lesson.teacherAgentId]); }
  } catch (error) { toast(error.message); }
}));

applyLanguage();
fetch("/health").then(response => { if (!response.ok) throw new Error(); document.querySelector("#server-dot").classList.add("online"); document.querySelector("#server-status").dataset.i18n = "serverAvailable"; applyLanguage(); }).catch(() => { document.querySelector("#server-status").dataset.i18n = "serverUnavailable"; applyLanguage(); });
if (state.pin) loadOverview().then(() => { elements.authPanel.hidden = true; elements.workspace.hidden = false; }).catch(() => logout());
window.setInterval(() => { if (state.pin) loadOverview().catch(() => {}); }, 10000);
