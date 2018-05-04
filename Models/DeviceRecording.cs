using System;

public class DeviceRecording
{
    public double Version { get; set; }

    public Guid DeviceId { get; set; }

    public Guid LocationId { get; set; }

    public string DeviceLocationComposite { get; set; }

    public string SubmitDay { get; set; }

    public string SubmitHour { get; set; }

    public string SubmitMinute { get; set; }

    public string SubmitSecond { get; set; }

    public double TemperatureCelsius { get; set; }

    public double Humidity { get; set; }
}