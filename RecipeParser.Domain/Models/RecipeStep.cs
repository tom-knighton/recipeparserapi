namespace RecipeParser.Domain.Models;

public class RecipeStep
{
    public string Step { get; set; }
    public ICollection<RecipeStepTime> Times { get; set; } = [];
    public ICollection<RecipeStepTemperature> Temperatures { get; set; } = [];
}

public class RecipeStepTime
{
    public decimal TimeInSeconds { get; set; }
    public string TimeText { get; set; }
    public string TimeUnitText { get; set; }
}

public class RecipeStepTemperature
{
    public decimal Temperature { get; set; }
    public string TemperatureUnitText { get; set; }
    public string TemperatureText { get; set; }
}