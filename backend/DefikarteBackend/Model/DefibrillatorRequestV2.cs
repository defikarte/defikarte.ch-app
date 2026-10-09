namespace DefikarteBackend.Model
{
    public class DefibrillatorRequestV2
    {
        public long? Id { get; set; }

        public double Longitude { get; set; }

        public double Latitude { get; set; }

        public string? Location { get; set; }

        public string? Reporter { get; set; }

        public string? Description { get; set; }

        public string? Access { get; set; }

        public string? Operator { get; set; }

        public string? OperatorPhone { get; set; }

        public string? OpeningHours { get; set; }

        public string? EmergencyPhone { get; set; }

        public string? Indoor { get; set; }

        public string? Source { get; set; }

        public string? Level { get; set; }
    }
}