const translations = {
  "pt-PT": {
    kicker: "ADMINISTRAÇÃO LOCAL", teacherConsole: "Consola do professor", language: "Idioma",
    adminAccess: "Acesso administrativo", adminHelp: "Introduza o token administrativo guardado em bootstrap-secrets.json.",
    adminToken: "Token administrativo", enter: "Entrar", cloud: "CLOUD", cloudSync: "Sincronização central",
    syncUsername: "Utilizador de sincronização", syncPassword: "Palavra-passe", connect: "Ligar à cloud",
    syncNow: "Sincronizar agora", disconnect: "Remover ligação", settingsKicker: "DEFINIÇÕES",
    serverSettings: "Configuração sincronizada", settingsHelp: "As alterações são aplicadas localmente e enviadas para a gestão central.",
    requireHttps: "HTTPS obrigatório", lessonDuration: "Duração predefinida da aula (minutos)", frameSize: "Tamanho máximo do frame (bytes)",
    frameInterval: "Intervalo mínimo entre frames (ms)", allowedApps: "Aplicações permitidas (um identificador por linha)",
    updates: "Atualizações automáticas", updateInterval: "Intervalo de atualização (horas)", manifestUrl: "URL do manifesto",
    thumbprint: "Impressão digital Authenticode", saveSettings: "Guardar e sincronizar", yes: "Sim", no: "Não",
    enabled: "Ativas", disabled: "Desativadas", disconnected: "Desligado", connected: "Ligado", synchronized: "Sincronizado",
    conflict: "Conflito resolvido", error: "Erro", school: "Escola", serverId: "Servidor", instance: "Instância",
    revision: "Revisão", lastSync: "Última sincronização", never: "Nunca", cloudUrl: "Endereço cloud",
    credentialsRequired: "Introduza as credenciais técnicas criadas no portal central.", connectedTo: "Ligado à gestão central.",
    loginFailed: "Token administrativo inválido.", connectionSaved: "Ligação à cloud concluída.", syncComplete: "Sincronização concluída.",
    settingsSaved: "Definições guardadas e sincronizadas.", disconnectedOk: "Ligação à cloud removida.",
    confirmDisconnect: "Remover deste servidor a ligação e o token da cloud?", requestFailed: "Não foi possível concluir o pedido."
  },
  en: {
    kicker: "LOCAL ADMINISTRATION", teacherConsole: "Teacher console", language: "Language",
    adminAccess: "Administrative access", adminHelp: "Enter the administrative token stored in bootstrap-secrets.json.",
    adminToken: "Administrative token", enter: "Sign in", cloud: "CLOUD", cloudSync: "Central synchronization",
    syncUsername: "Synchronization username", syncPassword: "Password", connect: "Connect to cloud",
    syncNow: "Synchronize now", disconnect: "Remove connection", settingsKicker: "SETTINGS",
    serverSettings: "Synchronized configuration", settingsHelp: "Changes are applied locally and sent to central management.",
    requireHttps: "Require HTTPS", lessonDuration: "Default lesson duration (minutes)", frameSize: "Maximum frame size (bytes)",
    frameInterval: "Minimum frame interval (ms)", allowedApps: "Allowed applications (one identifier per line)",
    updates: "Automatic updates", updateInterval: "Update interval (hours)", manifestUrl: "Manifest URL",
    thumbprint: "Authenticode signer thumbprint", saveSettings: "Save and synchronize", yes: "Yes", no: "No",
    enabled: "Enabled", disabled: "Disabled", disconnected: "Disconnected", connected: "Connected", synchronized: "Synchronized",
    conflict: "Conflict resolved", error: "Error", school: "School", serverId: "Server", instance: "Instance",
    revision: "Revision", lastSync: "Last synchronization", never: "Never", cloudUrl: "Cloud address",
    credentialsRequired: "Enter the technical credentials created in the central portal.", connectedTo: "Connected to central management.",
    loginFailed: "Invalid administrative token.", connectionSaved: "Cloud connection completed.", syncComplete: "Synchronization completed.",
    settingsSaved: "Settings saved and synchronized.", disconnectedOk: "Cloud connection removed.",
    confirmDisconnect: "Remove the cloud connection and token from this server?", requestFailed: "The request could not be completed."
  },
  es: {
    kicker: "ADMINISTRACIÓN LOCAL", teacherConsole: "Consola del profesor", language: "Idioma",
    adminAccess: "Acceso administrativo", adminHelp: "Introduzca el token administrativo guardado en bootstrap-secrets.json.",
    adminToken: "Token administrativo", enter: "Entrar", cloud: "CLOUD", cloudSync: "Sincronización central",
    syncUsername: "Usuario de sincronización", syncPassword: "Contraseña", connect: "Conectar a la nube",
    syncNow: "Sincronizar ahora", disconnect: "Eliminar conexión", settingsKicker: "CONFIGURACIÓN",
    serverSettings: "Configuración sincronizada", settingsHelp: "Los cambios se aplican localmente y se envían a la gestión central.",
    requireHttps: "HTTPS obligatorio", lessonDuration: "Duración predeterminada de la clase (minutos)", frameSize: "Tamaño máximo del frame (bytes)",
    frameInterval: "Intervalo mínimo entre frames (ms)", allowedApps: "Aplicaciones permitidas (un identificador por línea)",
    updates: "Actualizaciones automáticas", updateInterval: "Intervalo de actualización (horas)", manifestUrl: "URL del manifiesto",
    thumbprint: "Huella digital Authenticode", saveSettings: "Guardar y sincronizar", yes: "Sí", no: "No",
    enabled: "Activas", disabled: "Desactivadas", disconnected: "Desconectado", connected: "Conectado", synchronized: "Sincronizado",
    conflict: "Conflicto resuelto", error: "Error", school: "Escuela", serverId: "Servidor", instance: "Instancia",
    revision: "Revisión", lastSync: "Última sincronización", never: "Nunca", cloudUrl: "Dirección cloud",
    credentialsRequired: "Introduzca las credenciales técnicas creadas en el portal central.", connectedTo: "Conectado a la gestión central.",
    loginFailed: "Token administrativo no válido.", connectionSaved: "Conexión a la nube completada.", syncComplete: "Sincronización completada.",
    settingsSaved: "Configuración guardada y sincronizada.", disconnectedOk: "Conexión a la nube eliminada.",
    confirmDisconnect: "¿Eliminar de este servidor la conexión y el token de la nube?", requestFailed: "No se pudo completar la solicitud."
  }
};

const languageSelect = document.querySelector("#language");
const authPanel = document.querySelector("#auth-panel");
const workspace = document.querySelector("#admin-workspace");
const toast = document.querySelector("#toast");
let language = localStorage.getItem("iwebclassroom-language") || "pt-PT";
let adminToken = sessionStorage.getItem("iwebclassroom-admin-token") || "";
let currentModel = null;

function t(key) { return translations[language]?.[key] || translations["pt-PT"][key] || key; }

function applyLanguage() {
  document.documentElement.lang = language;
  document.querySelectorAll("[data-i18n]").forEach(element => { element.textContent = t(element.dataset.i18n); });
  document.querySelectorAll("[data-i18n-placeholder]").forEach(element => { element.placeholder = t(element.dataset.i18nPlaceholder); });
  languageSelect.value = language;
  if (currentModel) render(currentModel);
}

function notify(message) {
  toast.textContent = message;
  toast.classList.add("visible");
  window.clearTimeout(notify.timer);
  notify.timer = window.setTimeout(() => toast.classList.remove("visible"), 3500);
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", "X-Admin-Token": adminToken, ...(options.headers || {}) }
  });
  const body = await response.json().catch(() => ({}));
  if (response.status === 401) {
    sessionStorage.removeItem("iwebclassroom-admin-token");
    adminToken = "";
    workspace.hidden = true;
    authPanel.hidden = false;
    throw new Error(t("loginFailed"));
  }
  if (!response.ok) throw new Error(body.error || body.detail || t("requestFailed"));
  return body;
}

function statusLabel(status) { return t(status) || status; }

function render(model) {
  currentModel = model;
  const configuration = model.configuration;
  const cloud = model.cloud;
  document.querySelector("#require-https").value = String(configuration.security.require_https);
  document.querySelector("#lesson-duration").value = configuration.classroom.default_lesson_duration_minutes;
  document.querySelector("#frame-size").value = configuration.classroom.maximum_frame_bytes;
  document.querySelector("#frame-interval").value = configuration.classroom.minimum_frame_interval_milliseconds;
  document.querySelector("#allowed-apps").value = configuration.classroom.allowed_application_ids.join("\n");
  document.querySelector("#update-enabled").value = String(configuration.update.enabled);
  document.querySelector("#update-interval").value = configuration.update.check_interval_hours;
  document.querySelector("#manifest-url").value = configuration.update.manifest_url;
  document.querySelector("#signer-thumbprint").value = configuration.update.signer_thumbprint;

  const badge = document.querySelector("#cloud-badge");
  badge.textContent = statusLabel(cloud.status);
  badge.className = `status-pill ${cloud.connected ? "success" : cloud.status === "error" ? "failure" : ""}`;
  const rows = [
    [t("school"), cloud.schoolName || "—"], [t("serverId"), cloud.serverId || "—"],
    [t("instance"), cloud.instanceUid], [t("revision"), cloud.configurationRevision],
    [t("lastSync"), cloud.lastSyncUtc ? new Date(cloud.lastSyncUtc).toLocaleString(language) : t("never")],
    [t("cloudUrl"), cloud.cloudUrl]
  ];
  document.querySelector("#cloud-details").innerHTML = rows.map(([label, value]) =>
    `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(String(value))}</strong></div>`).join("") +
    (cloud.lastError ? `<p class="error-message">${escapeHtml(cloud.lastError)}</p>` : "");
  document.querySelector("#cloud-connect-form").hidden = cloud.connected;
  document.querySelector("#sync-now").hidden = !cloud.connected;
  document.querySelector("#disconnect-cloud").hidden = !cloud.connected;
}

function escapeHtml(value) {
  const element = document.createElement("span");
  element.textContent = value;
  return element.innerHTML;
}

async function load() {
  const model = await api("/api/v1/settings");
  authPanel.hidden = true;
  workspace.hidden = false;
  render(model);
}

document.querySelector("#auth-form").addEventListener("submit", async event => {
  event.preventDefault();
  adminToken = document.querySelector("#admin-token").value;
  try {
    await load();
    sessionStorage.setItem("iwebclassroom-admin-token", adminToken);
    document.querySelector("#admin-token").value = "";
  } catch (error) { notify(error.message); }
});

document.querySelector("#cloud-connect-form").addEventListener("submit", async event => {
  event.preventDefault();
  try {
    await api("/api/v1/cloud/connect", { method: "POST", body: JSON.stringify({
      username: document.querySelector("#sync-username").value,
      password: document.querySelector("#sync-password").value
    }) });
    document.querySelector("#sync-password").value = "";
    await load();
    notify(t("connectionSaved"));
  } catch (error) { notify(error.message); }
});

document.querySelector("#sync-now").addEventListener("click", async () => {
  try { await api("/api/v1/cloud/sync", { method: "POST" }); await load(); notify(t("syncComplete")); }
  catch (error) { notify(error.message); }
});

document.querySelector("#disconnect-cloud").addEventListener("click", async () => {
  if (!window.confirm(t("confirmDisconnect"))) return;
  try { await api("/api/v1/cloud/connection", { method: "DELETE" }); await load(); notify(t("disconnectedOk")); }
  catch (error) { notify(error.message); }
});

document.querySelector("#settings-form").addEventListener("submit", async event => {
  event.preventDefault();
  const allowed = document.querySelector("#allowed-apps").value.split(/\r?\n|,/).map(value => value.trim()).filter(Boolean);
  const settings = {
    security: { require_https: document.querySelector("#require-https").value === "true" },
    classroom: {
      maximum_frame_bytes: Number(document.querySelector("#frame-size").value),
      minimum_frame_interval_milliseconds: Number(document.querySelector("#frame-interval").value),
      default_lesson_duration_minutes: Number(document.querySelector("#lesson-duration").value),
      allowed_application_ids: allowed
    },
    update: {
      enabled: document.querySelector("#update-enabled").value === "true",
      manifest_url: document.querySelector("#manifest-url").value.trim(),
      signer_thumbprint: document.querySelector("#signer-thumbprint").value.trim(),
      check_interval_hours: Number(document.querySelector("#update-interval").value)
    }
  };
  try {
    const model = await api("/api/v1/settings", { method: "PUT", body: JSON.stringify(settings) });
    render(model);
    notify(t("settingsSaved"));
  } catch (error) { notify(error.message); }
});

languageSelect.addEventListener("change", () => {
  language = languageSelect.value;
  localStorage.setItem("iwebclassroom-language", language);
  applyLanguage();
});

applyLanguage();
if (adminToken) load().catch(error => notify(error.message));
