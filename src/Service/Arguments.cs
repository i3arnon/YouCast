namespace Service
{
    public sealed class Arguments
    {
        public string PlaylistId { get; }
        public string Encoding { get; }
        public int MaxLength { get; }
        public bool IsPopular { get; }

        public Arguments(string playlistId, string encoding, int maxLength, bool isPopular)
        {
            PlaylistId = playlistId;
            Encoding = encoding;
            MaxLength = maxLength;
            IsPopular = isPopular;

            if (MaxLength <= 0)
            {
                MaxLength = int.MaxValue;
            }
        }

        private bool Equals(Arguments other) => 
            string.Equals(PlaylistId, other.PlaylistId) && 
            string.Equals(Encoding, other.Encoding) && 
            MaxLength == other.MaxLength && 
            IsPopular == other.IsPopular;

        public override bool Equals(object obj) => 
            ReferenceEquals(this, obj) || obj is Arguments other && 
            Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (PlaylistId != null ? PlaylistId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Encoding != null ? Encoding.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ MaxLength;
                hashCode = (hashCode * 397) ^ IsPopular.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString() =>
            string.Join(",", PlaylistId, Encoding.ToLower(), MaxLength, IsPopular);
    }
}
