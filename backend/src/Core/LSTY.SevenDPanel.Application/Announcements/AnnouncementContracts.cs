using System;

namespace LSTY.SevenDPanel.Application.Announcements
{
    public sealed record AnnouncementMessage(string MessageText);

    public sealed class AnnouncementValidationException : Exception
    {
        public AnnouncementValidationException()
            : base("The announcement must contain between 1 and 500 plain text characters.")
        {
        }

        public string Code => "announcement_invalid";
    }
}
