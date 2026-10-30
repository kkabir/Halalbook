namespace HalalBook.Shared;

/// <summary>
/// The HalalBook Community Agreement. Bump <see cref="Version"/> whenever the text changes in a way people must re-accept;
/// everyone is then asked to read and agree again on their next visit.
/// Lines starting with "- " render as bullets. Have a lawyer review this text before relying on it.
/// </summary>
public static class Terms
{
    public const string Version = "2026-10-30";

    public record Page(string Title, string Body);

    public static readonly IReadOnlyList<Page> Pages =
    [
        new("Welcome to HalalBook", """
            Assalamu alaikum, and welcome. HalalBook is a social network built for the Muslim community and for everyone who wants a modest, kind and beneficial place to connect.
            This Community Agreement explains what you can expect from us and what we expect from you. Please read every page. By selecting "I agree" you accept these terms.
            - You must be at least 13 years old to use HalalBook. If you are under 18, a parent or guardian should review this agreement with you.
            - Use your real name and accurate information. One personal account per person.
            - Keep your password private. You are responsible for activity on your account.
            - HalalBook is free to use. We may add optional features in the future and will always tell you before anything costs money.
            """),
        new("Our values: adab and modesty", """
            HalalBook is guided by Islamic values of honesty, modesty, kindness and respect. We ask every member to practice good adab (manners).
            - Speak good or remain silent. Disagree respectfully; criticize ideas, not people.
            - Respect every person regardless of school of thought, ethnicity, nationality, gender or level of practice.
            - Do not call other Muslims disbelievers (takfir) or spread hatred between groups.
            - Protect privacy: do not share other people's private information, photos or messages without permission.
            - Be truthful: do not spread rumors, fake news or fabricated religious claims.
            """),
        new("Content that is not allowed", """
            To keep HalalBook safe and halal, the following are strictly prohibited in posts, photos, videos, stories, reels, live streams, comments, messages, profiles and links:
            - Nudity, sexual or pornographic content, sexual services, or links to adult websites, whether real, drawn or AI-generated.
            - Immodest or sexually suggestive photos and videos.
            - Hate speech, harassment, bullying, threats, or mocking of religion, prophets or any people.
            - Violent extremism, terrorism, graphic violence or gore, or praise of such acts.
            - Promotion or sale of alcohol, drugs, gambling, interest-based scams or other haram products.
            - Scams, impersonation, spam, malware or attempts to hack accounts.
            - Content that exploits or endangers children in any way. We report such content to the authorities.
            - Copyrighted material you do not have the right to share.
            """),
        new("How we keep the community safe", """
            We use a combination of people and technology to enforce this agreement.
            - Automated checks, including AI, review text, photos and video frames when they are uploaded. Content may be blocked or held for a moderator before it appears.
            - Members can report content. Content reported by several people may be hidden until a moderator reviews it.
            - Moderators may remove content, limit features, or suspend accounts. Repeated violations lead to suspension.
            - Serious violations, such as child exploitation or credible threats, result in immediate removal and may be reported to law enforcement.
            - If you believe we made a mistake, contact us through the Help & feedback page to appeal.
            """),
        new("Your content and your privacy", """
            You own the content you post. To run HalalBook, you give us permission to store, display and share your content with the audience you choose.
            - You choose who sees each post: public, friends, specific friends, friend lists or groups.
            - You control who can message you, send you friend requests, post on your profile and see your active status.
            - We collect the information needed to provide the service, such as your account details, content, and basic technical information like your device and IP address for security.
            - We do not sell your personal information. We never show your private messages to advertisers.
            - You can download or delete your account and content by contacting us. Deleted content may remain in backups for a limited time.
            """),
        new("AI features and Noor", """
            HalalBook includes AI features powered by trusted providers, such as Noor the assistant, writing help, translations, summaries, smart replies and content checks.
            - Noor gives general information about Islam with references where possible, but Noor is not a mufti or scholar. For personal religious rulings, consult a qualified scholar.
            - AI can make mistakes. Check important information before relying on it.
            - Text and images you send to AI features may be processed by our AI provider only to deliver the feature.
            - Do not use AI features to create content that breaks this agreement.
            """),
        new("Live, calls and messaging", """
            Live shows, podcasts, events, video calls and messages must follow the same rules as posts.
            - Do not record or share other people's calls or live streams without their permission.
            - Hosts are responsible for their live sessions and should end them if rules are broken.
            - Do not send unwanted messages, spam or repeated requests to people who have not accepted you.
            - Report or block anyone who makes you uncomfortable. Blocked people cannot contact you.
            """),
        new("Ending, changes and contact", """
            You can stop using HalalBook at any time. We may suspend or end accounts that break this agreement or the law.
            - HalalBook is provided "as is". We work hard to keep it available and safe, but we cannot guarantee it will always be error-free.
            - To the extent allowed by law, HalalBook is not liable for content posted by members or for indirect losses.
            - We may update this agreement. When we make important changes, we will ask you to read and accept it again.
            - Questions, appeals or legal requests: support@halalbook.app or the Help & feedback page.
            JazakAllahu khayran for helping us build a community that pleases Allah and benefits people.
            """),
    ];
}
