namespace IWebClassroom.Server.Localization;

public static class ApiText
{
    private static readonly IReadOnlyDictionary<string, string[]> Values = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["EnrollmentNotConfigured"] = ["A inscrição não está configurada neste servidor.", "Enrollment is not configured on this server.", "La inscripción no está configurada en este servidor."],
        ["InvalidText"] = ["O campo deve conter entre {0} e {1} caracteres imprimíveis.", "The field must contain between {0} and {1} printable characters.", "El campo debe contener entre {0} y {1} caracteres imprimibles."],
        ["InvalidRole"] = ["A função não é válida.", "The role is invalid.", "La función no es válida."],
        ["NoTarget"] = ["Não foi selecionado nenhum agente válido.", "No valid target agent was selected.", "No se ha seleccionado ningún agente válido."],
        ["SelectAgents"] = ["Selecione entre 1 e 200 agentes.", "Select between 1 and 200 agents.", "Seleccione entre 1 y 200 agentes."],
        ["InvalidCommand"] = ["O tipo de comando não é válido.", "The command type is invalid.", "El tipo de comando no es válido."],
        ["ObjectPayload"] = ["Os dados do comando devem ser um objeto JSON.", "The command payload must be a JSON object.", "Los datos del comando deben ser un objeto JSON."],
        ["MessageLength"] = ["A mensagem deve conter entre 1 e 500 caracteres.", "The message must contain between 1 and 500 characters.", "El mensaje debe contener entre 1 y 500 caracteres."],
        ["AppNotAllowed"] = ["A aplicação não é permitida pelo servidor.", "The application is not allowed by the server.", "La aplicación no está permitida por el servidor."],
        ["UnsupportedCommand"] = ["O tipo de comando não é suportado.", "The command type is not supported.", "El tipo de comando no es compatible."],
        ["InvalidRoom"] = ["A sala deve conter entre 1 e 50 caracteres.", "The room must contain between 1 and 50 characters.", "El aula debe contener entre 1 y 50 caracteres."],
        ["InvalidDuration"] = ["A duração da aula deve estar entre 15 e 480 minutos.", "The lesson duration must be between 15 and 480 minutes.", "La duración de la clase debe estar entre 15 y 480 minutos."],
        ["InvalidRoomPin"] = ["O PIN da sala não é válido ou a sala já terminou.", "The room PIN is invalid or the room has already ended.", "El PIN de la sala no es válido o la sala ya ha terminado."],
        ["StudentNameRequired"] = ["O nome do aluno é obrigatório e pode ter até 100 caracteres.", "The student name is required and can contain up to 100 characters.", "El nombre del alumno es obligatorio y puede tener hasta 100 caracteres."],
        ["InvalidRoomCode"] = ["O código da sala deve conter exatamente 6 algarismos.", "The room code must contain exactly 6 digits.", "El código del aula debe contener exactamente 6 dígitos."],
        ["CloudRoomUnavailable"] = ["Não foi possível procurar a sala noutros servidores porque a cloud está indisponível. As salas deste servidor continuam disponíveis.", "The room could not be searched on other servers because the cloud is unavailable. Rooms on this server remain available.", "No se pudo buscar el aula en otros servidores porque la nube no está disponible. Las aulas de este servidor siguen disponibles."],
        ["RoamingUnavailable"] = ["Não foi possível autorizar a ligação ao servidor da sala. Confirme o código e tente novamente.", "The connection to the room server could not be authorized. Check the code and try again.", "No se pudo autorizar la conexión al servidor del aula. Compruebe el código e inténtelo de nuevo."],
        ["ServerAtCapacity"] = ["Este servidor não está a aceitar novas aulas ou atingiu a capacidade definida na gestão central.", "This server is not accepting new lessons or has reached the capacity configured in central management.", "Este servidor no acepta nuevas clases o ha alcanzado la capacidad configurada en la gestión central."],
        ["InvalidTeacher"] = ["Selecione um posto de professor válido.", "Select a valid teacher workstation.", "Seleccione un puesto de profesor válido."],
        ["RoomInUse"] = ["A sala ou o posto de professor já tem uma aula ativa.", "The room or teacher workstation already has an active lesson.", "El aula o el puesto del profesor ya tiene una clase activa."],
        ["LessonNotFound"] = ["A aula já terminou ou não existe.", "The lesson has ended or does not exist.", "La clase ha terminado o no existe."],
        ["NoLessonTarget"] = ["Nenhum dos computadores selecionados pertence a esta aula.", "None of the selected computers belongs to this lesson.", "Ninguno de los equipos seleccionados pertenece a esta clase."]
    };

    public static string Get(HttpRequest request, string key, params object[] arguments)
    {
        var language = request.GetTypedHeaders().AcceptLanguage?
            .OrderByDescending(item => item.Quality ?? 1)
            .Select(item => item.Value.Value)
            .FirstOrDefault() ?? "pt-PT";
        var index = language.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? 2
            : language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, Values[key][index], arguments);
    }
}
