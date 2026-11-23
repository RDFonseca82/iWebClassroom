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
        ["UnsupportedCommand"] = ["O tipo de comando não é suportado.", "The command type is not supported.", "El tipo de comando no es compatible."]
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
