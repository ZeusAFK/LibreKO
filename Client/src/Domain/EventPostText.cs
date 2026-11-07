using System;
using LibreKO.Network;

namespace LibreKO.Domain;

public static class EventPostText
{
    public const int PostsPerPage = 3;
    public const int HistoryPerPage = 10;

    public static readonly (int Text, string Fallback) Today = (33216, "Number of Posts registered for the day");
    public static readonly (int Text, string Fallback) Total = (33217, "Total Number of registered posts");
    public static readonly (int Text, string Fallback) History = (33220, "History");
    public static readonly (int Text, string Fallback) UpdateFailed = (33215, "Data Update Failed");

    private const string HowToFallback = "- How to Participate - \n 1. Log in with the character of the server you want to receive rewards. \n 2. Open the Event UI through Akara's Altar NPC. \n 3. Enter {0} in the input field.";
    private const string NoteFallback = "- Note - \n You can only participate once a day.";

    public static (int Text, string Fallback) Title(EventPostEvent eventId) => eventId == EventPostEvent.ContinuePlaying
        ? (33223, Phrase(eventId))
        : (33213, Phrase(eventId));

    public static (int Text, string Fallback) HowTo(EventPostEvent eventId) =>
        (eventId == EventPostEvent.ContinuePlaying ? 33221 : 33211, string.Format(HowToFallback, Phrase(eventId)));

    public static (int Text, string Fallback) Note(EventPostEvent eventId) =>
        (eventId == EventPostEvent.ContinuePlaying ? 33222 : 33212, NoteFallback);

    public static string Phrase(EventPostEvent eventId) =>
        eventId == EventPostEvent.ContinuePlaying ? "I Will Continue Playing Knight Online" : "I Love Knight Online";

    public static (int Text, string Fallback)? Refusal(EventPostResult result) => result switch
    {
        EventPostResult.Accepted => null,
        EventPostResult.WrongText => (33218, "You have entered incorrect characters. Please check again."),
        EventPostResult.AlreadyPosted => (33219, "You have already posted for the day. Please try tomorrow."),
        EventPostResult.TryLater => (16810, "Try again later"),
        _ => (33214, "Registration Failed"),
    };

    public static string Day(uint unixTime) => Local(unixTime).ToString("dd.MM.yyyy");

    public static string Moment(uint unixTime) => Local(unixTime).ToString("dd.MM.yyyy HH:mm");

    public static string Period(uint start, uint end) => $"{Day(start)} ~ {Day(end)}";

    private static DateTime Local(uint unixTime) => DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
}
