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

        public override string ToString() =>
            string.Join(",", PlaylistId, Encoding.ToLower(), MaxLength, IsPopular);
    }
}
