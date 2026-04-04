namespace Geotrack.Server.Requests
{
    public class ExploreByLocationRequest
    {
        public double Latitude {get; set;}
        public double Longitude {get; set;}
        public double Radius { get; set; } = 10.0;
    }
}
