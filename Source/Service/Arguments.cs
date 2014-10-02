namespace Service
{
    public sealed class Arguments
    {
        #region Properties

        public string PlaylistId { get; set; }
        public string Encoding { get; set; }
        public int MaxLength { get; set; }
        public bool IsPopular { get; set; }

        #endregion

        #region Constructors

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

        #endregion

        #region MyRegion

        public override string ToString()
        {
            return string.Join(",", PlaylistId, Encoding.ToLower(), MaxLength, IsPopular);
        }

        #endregion
    }
}
