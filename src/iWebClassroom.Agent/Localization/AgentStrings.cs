using System.Globalization;
using IWebClassroom.Agent.Configuration;

namespace IWebClassroom.Agent.Localization;

public static class AgentStrings
{
    private static readonly string Language = InstallerPreferences.Language();

    private static readonly IReadOnlyDictionary<string, string[]> Values = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [nameof(AgentWindows)] = ["AGENTE WINDOWS", "WINDOWS AGENT", "AGENTE WINDOWS"],
        [nameof(Disconnected)] = ["Desligado", "Disconnected", "Desconectado"],
        [nameof(Server)] = ["Servidor", "Server", "Servidor"],
        [nameof(ServerHint)] = ["Exemplo: https://servidor-escola:7443", "Example: https://school-server:7443", "Ejemplo: https://servidor-centro:7443"],
        [nameof(EnrollmentCode)] = ["Código de inscrição", "Enrollment code", "Código de inscripción"],
        [nameof(EnrollmentHint)] = ["Só é necessário na primeira ligação", "Only required for the first connection", "Solo es necesario en la primera conexión"],
        [nameof(ComputerName)] = ["Nome deste computador", "Computer name", "Nombre de este equipo"],
        [nameof(Classroom)] = ["Sala", "Classroom", "Aula"],
        [nameof(DefaultRoom)] = ["Sala predefinida", "Default room", "Aula predeterminada"],
        [nameof(WorkstationType)] = ["Tipo de posto", "Workstation type", "Tipo de puesto"],
        [nameof(SharedComputer)] = ["Computador partilhado", "Shared computer", "Equipo compartido"],
        [nameof(SharedComputerHint)] = ["Mostra as salas temporárias disponíveis ao aluno", "Shows the temporary rooms available to the student", "Muestra las aulas temporales disponibles al alumno"],
        [nameof(AvailableRooms)] = ["Salas disponíveis", "Available rooms", "Aulas disponibles"],
        [nameof(RefreshRooms)] = ["Atualizar", "Refresh", "Actualizar"],
        [nameof(RoomPin)] = ["PIN da sala", "Room PIN", "PIN del aula"],
        [nameof(JoinRoom)] = ["Entrar na sala", "Join room", "Entrar en el aula"],
        [nameof(NoRoomsAvailable)] = ["Não existem salas disponíveis neste momento.", "There are no available rooms at this time.", "No hay aulas disponibles en este momento."],
        [nameof(SelectRoom)] = ["Selecione uma sala disponível.", "Select an available room.", "Seleccione un aula disponible."],
        [nameof(InvalidRoomPin)] = ["O PIN da sala não é válido ou a sala já terminou.", "The room PIN is invalid or the room has already ended.", "El PIN del aula no es válido o el aula ya ha terminado."],
        [nameof(RoomJoined)] = ["Entrou na sala {0}.", "Joined room {0}.", "Ha entrado en el aula {0}."],
        [nameof(Student)] = ["Aluno", "Student", "Alumno"],
        [nameof(Teacher)] = ["Professor", "Teacher", "Profesor"],
        [nameof(Identity)] = ["Identidade", "Identity", "Identidad"],
        [nameof(NotEnrolled)] = ["Ainda não inscrito", "Not enrolled yet", "Aún no inscrito"],
        [nameof(Connect)] = ["Ligar ao servidor", "Connect to server", "Conectar al servidor"],
        [nameof(ShareScreen)] = ["Partilhar ecrã", "Share screen", "Compartir pantalla"],
        [nameof(Stop)] = ["Parar", "Stop", "Detener"],
        [nameof(SharedScreen)] = ["Ecrã partilhado pelo professor", "Screen shared by the teacher", "Pantalla compartida por el profesor"],
        [nameof(Connecting)] = ["A ligar…", "Connecting…", "Conectando…"],
        [nameof(Connected)] = ["Ligado", "Connected", "Conectado"],
        [nameof(Reconnecting)] = ["A restabelecer ligação…", "Reconnecting…", "Reconectando…"],
        [nameof(ConnectionError)] = ["Erro de ligação", "Connection error", "Error de conexión"],
        [nameof(AppTitle)] = ["iWebClassroom", "iWebClassroom", "iWebClassroom"],
        [nameof(TeacherMessageTitle)] = ["Mensagem do professor", "Teacher message", "Mensaje del profesor"],
        [nameof(ScreenSharing)] = ["Ecrã em partilha", "Screen is being shared", "Pantalla compartida"],
        [nameof(InstallingUpdate)] = ["A instalar atualização…", "Installing update…", "Instalando actualización…"],
        [nameof(UpdatePrefix)] = ["Atualização", "Update", "Actualización"],
        [nameof(FailurePrefix)] = ["Falha", "Failure", "Error"],
        [nameof(InvalidServer)] = ["O servidor tem de usar HTTPS. HTTP só é permitido em localhost para testes.", "The server must use HTTPS. HTTP is only allowed on localhost for testing.", "El servidor debe usar HTTPS. HTTP solo se permite en localhost para pruebas."],
        [nameof(EnterEnrollmentCode)] = ["Introduza o código de inscrição para registar ou alterar este agente.", "Enter the enrollment code to register or change this agent.", "Introduzca el código de inscripción para registrar o modificar este agente."],
        [nameof(EnrollmentInvalid)] = ["O código de inscrição não é válido.", "The enrollment code is invalid.", "El código de inscripción no es válido."],
        [nameof(EnrollmentUnavailable)] = ["O servidor ainda não tem a inscrição configurada.", "Enrollment is not configured on the server yet.", "La inscripción aún no está configurada en el servidor."],
        [nameof(EnrollmentFailed)] = ["A inscrição falhou ({0}).", "Enrollment failed ({0}).", "La inscripción ha fallado ({0})."],
        [nameof(EmptyEnrollmentResponse)] = ["O servidor devolveu uma resposta de inscrição vazia.", "The server returned an empty enrollment response.", "El servidor devolvió una respuesta de inscripción vacía."],
        [nameof(CertificateBootstrapFailed)] = ["Não foi possível obter o certificado seguro do servidor local.", "The local server security certificate could not be obtained.", "No se pudo obtener el certificado seguro del servidor local."],
        [nameof(InvalidServerCertificate)] = ["O certificado apresentado não pertence a um servidor iWebClassroom válido.", "The certificate presented does not belong to a valid iWebClassroom server.", "El certificado presentado no pertenece a un servidor iWebClassroom válido."],
        [nameof(RequiredField)] = ["Preencha o campo {0}.", "Complete the {0} field.", "Complete el campo {0}."],
        [nameof(ComputerNameField)] = ["nome do computador", "computer name", "nombre del equipo"],
        [nameof(ClassroomField)] = ["sala", "classroom", "aula"],
        [nameof(AppNotAllowed)] = ["A aplicação '{0}' não está autorizada neste computador.", "Application '{0}' is not allowed on this computer.", "La aplicación '{0}' no está autorizada en este equipo."],
        [nameof(AppNotFound)] = ["O executável autorizado para '{0}' não foi encontrado.", "The allowed executable for '{0}' was not found.", "No se encontró el ejecutable autorizado para '{0}'."],
        [nameof(ScreenCaptureFailed)] = ["Não foi possível capturar o ecrã.", "The screen could not be captured.", "No se pudo capturar la pantalla."],
        [nameof(UpdateHttpsRequired)] = ["A atualização foi rejeitada porque não utiliza HTTPS.", "The update was rejected because it does not use HTTPS.", "La actualización fue rechazada porque no utiliza HTTPS."],
        [nameof(UpdateHashInvalid)] = ["A atualização foi rejeitada porque o hash SHA-256 não corresponde.", "The update was rejected because its SHA-256 hash does not match.", "La actualización fue rechazada porque el hash SHA-256 no coincide."],
        [nameof(UpdateSignatureInvalid)] = ["A atualização foi rejeitada porque a assinatura digital não é válida.", "The update was rejected because its digital signature is invalid.", "La actualización fue rechazada porque la firma digital no es válida."],
        [nameof(UpdaterMissing)] = ["O componente de atualização não está instalado.", "The update component is not installed.", "El componente de actualización no está instalado."]
    };

    private static int Index => Language == "pt" ? 0 : Language == "es" ? 2 : 1;
    private static string Text(string key) => Values[key][Index];

    public static string Format(string key, params object[] arguments) => string.Format(CultureInfo.CurrentUICulture, Text(key), arguments);
    public static string Get(string key) => Text(key);

    public static string AgentWindows => Text(nameof(AgentWindows));
    public static string Disconnected => Text(nameof(Disconnected));
    public static string Server => Text(nameof(Server));
    public static string ServerHint => Text(nameof(ServerHint));
    public static string EnrollmentCode => Text(nameof(EnrollmentCode));
    public static string EnrollmentHint => Text(nameof(EnrollmentHint));
    public static string ComputerName => Text(nameof(ComputerName));
    public static string Classroom => Text(nameof(Classroom));
    public static string DefaultRoom => Text(nameof(DefaultRoom));
    public static string WorkstationType => Text(nameof(WorkstationType));
    public static string SharedComputer => Text(nameof(SharedComputer));
    public static string SharedComputerHint => Text(nameof(SharedComputerHint));
    public static string AvailableRooms => Text(nameof(AvailableRooms));
    public static string RefreshRooms => Text(nameof(RefreshRooms));
    public static string RoomPin => Text(nameof(RoomPin));
    public static string JoinRoom => Text(nameof(JoinRoom));
    public static string NoRoomsAvailable => Text(nameof(NoRoomsAvailable));
    public static string SelectRoom => Text(nameof(SelectRoom));
    public static string InvalidRoomPin => Text(nameof(InvalidRoomPin));
    public static string RoomJoined => Text(nameof(RoomJoined));
    public static string Student => Text(nameof(Student));
    public static string Teacher => Text(nameof(Teacher));
    public static string Identity => Text(nameof(Identity));
    public static string NotEnrolled => Text(nameof(NotEnrolled));
    public static string Connect => Text(nameof(Connect));
    public static string ShareScreen => Text(nameof(ShareScreen));
    public static string Stop => Text(nameof(Stop));
    public static string SharedScreen => Text(nameof(SharedScreen));
    public static string Connecting => Text(nameof(Connecting));
    public static string Connected => Text(nameof(Connected));
    public static string Reconnecting => Text(nameof(Reconnecting));
    public static string ConnectionError => Text(nameof(ConnectionError));
    public static string AppTitle => Text(nameof(AppTitle));
    public static string TeacherMessageTitle => Text(nameof(TeacherMessageTitle));
    public static string ScreenSharing => Text(nameof(ScreenSharing));
    public static string InstallingUpdate => Text(nameof(InstallingUpdate));
    public static string UpdatePrefix => Text(nameof(UpdatePrefix));
    public static string FailurePrefix => Text(nameof(FailurePrefix));
    public static string InvalidServer => Text(nameof(InvalidServer));
    public static string EnterEnrollmentCode => Text(nameof(EnterEnrollmentCode));
    public static string EnrollmentInvalid => Text(nameof(EnrollmentInvalid));
    public static string EnrollmentUnavailable => Text(nameof(EnrollmentUnavailable));
    public static string EnrollmentFailed => Text(nameof(EnrollmentFailed));
    public static string EmptyEnrollmentResponse => Text(nameof(EmptyEnrollmentResponse));
    public static string CertificateBootstrapFailed => Text(nameof(CertificateBootstrapFailed));
    public static string InvalidServerCertificate => Text(nameof(InvalidServerCertificate));
    public static string RequiredField => Text(nameof(RequiredField));
    public static string ComputerNameField => Text(nameof(ComputerNameField));
    public static string ClassroomField => Text(nameof(ClassroomField));
    public static string AppNotAllowed => Text(nameof(AppNotAllowed));
    public static string AppNotFound => Text(nameof(AppNotFound));
    public static string ScreenCaptureFailed => Text(nameof(ScreenCaptureFailed));
    public static string UpdateHttpsRequired => Text(nameof(UpdateHttpsRequired));
    public static string UpdateHashInvalid => Text(nameof(UpdateHashInvalid));
    public static string UpdateSignatureInvalid => Text(nameof(UpdateSignatureInvalid));
    public static string UpdaterMissing => Text(nameof(UpdaterMissing));
}
