using System;
using System.Collections.Generic;
using System.Text;

namespace ZoneScrobbler
{
    class Track : IEquatable<Track>, IComparable<Track>
    {
        public Track(string sTitle, string sArtist, DateTime dtStartTime)
        {
            this.m_sTitle = sTitle;
            this.m_sArtist = sArtist;
            this.m_dtStart = dtStartTime;
        }

        private string m_sTitle;

        public string Title
        {
            get { return m_sTitle; }
        }

        private string m_sArtist;

        public string Artist
        {
            get { return m_sArtist; }
        }

        private DateTime m_dtStart;

        public DateTime StartTime
        {
            get { return m_dtStart; }
        }

        #region IEquatable<Track> Members

        public bool Equals(Track other)
        {
            return this.Artist.Equals(other.Artist, StringComparison.InvariantCultureIgnoreCase) 
                && this.Title.Equals(other.Title, StringComparison.InvariantCultureIgnoreCase);
        }

        #endregion

        #region IComparable<Track> Members

        public int CompareTo(Track other)
        {
            return this.StartTime.CompareTo(other.StartTime);
        }

        #endregion
    }
}
