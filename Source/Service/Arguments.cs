namespace Service
{
    public sealed class Arguments
    {
        public string PlaylistId { get; set; }
        public string Encoding { get; set; }
        public int MaxLength { get; set; }
        public bool IsPopular { get; set; }

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

        public override string ToString()
        {
            return string.Join(",", PlaylistId, Encoding.ToLower(), MaxLength, IsPopular);
        }
    }
}
