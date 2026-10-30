namespace HalalBook.Web.Services;

/// <summary>
/// Hand-written translations of the most-seen interface text, so the main screens are instant in each language
/// (and work without an AI key). Everything else is translated by the API on first use.
/// Each language array follows the order of <see cref="Keys"/>.
/// </summary>
public static class BuiltInTranslations
{
    private static readonly string[] Keys =
    [
        "Home", "Friends", "Groups", "Pages", "Events", "Reels", "Live", "Messages", "Saved", "Prayer times",
        "Qibla finder", "Hijri calendar", "Ask Noor AI", "Menu", "Prayer", "Noor", "Notifications", "Settings & privacy", "Help & feedback", "Community Agreement",
        "Sign out", "My profile", "Search people, posts, groups, #hashtags", "Photo/Video", "Feeling", "Life event", "Post", "Share something beautiful…", "Like", "Comment",
        "Share", "Save", "Write a comment…", "Send", "See translation", "See original", "Catch me up", "Summarize", "Add story", "On this day",
        "Trending", "People you may know", "Online now", "Welcome back", "Sign in to continue to HalalBook", "Username or email", "Password", "Forgot password?", "Sign in", "Create an account",
        "Join HalalBook", "Your name", "Username", "Email", "Continue", "Language", "Reach the whole Ummah", "Translated from", "Moderation", "Admin console",
    ];

    private static readonly Dictionary<string, string[]> Values = new()
    {
        ["ar"] =
        [
            "الرئيسية", "الأصدقاء", "المجموعات", "الصفحات", "الفعاليات", "المقاطع", "مباشر", "الرسائل", "المحفوظات", "مواقيت الصلاة",
            "اتجاه القبلة", "التقويم الهجري", "اسأل نور", "القائمة", "الصلاة", "نور", "الإشعارات", "الإعدادات والخصوصية", "المساعدة والملاحظات", "ميثاق المجتمع",
            "تسجيل الخروج", "ملفي الشخصي", "ابحث عن أشخاص ومنشورات ومجموعات و#وسوم", "صورة/فيديو", "الشعور", "حدث في حياتي", "نشر", "شارك شيئًا جميلًا…", "إعجاب", "تعليق",
            "مشاركة", "حفظ", "اكتب تعليقًا…", "إرسال", "عرض الترجمة", "عرض الأصل", "ما فاتني", "لخّص", "أضف قصة", "في مثل هذا اليوم",
            "الأكثر تداولًا", "أشخاص قد تعرفهم", "متصل الآن", "مرحبًا بعودتك", "سجّل الدخول للمتابعة إلى حلال بوك", "اسم المستخدم أو البريد الإلكتروني", "كلمة المرور", "نسيت كلمة المرور؟", "تسجيل الدخول", "إنشاء حساب",
            "انضم إلى حلال بوك", "اسمك", "اسم المستخدم", "البريد الإلكتروني", "متابعة", "اللغة", "ليصل إلى الأمة كلها", "مترجم من", "الإشراف", "لوحة الإدارة",
        ],
        ["ur"] =
        [
            "ہوم", "دوست", "گروپس", "صفحات", "تقریبات", "ریلز", "لائیو", "پیغامات", "محفوظ", "نماز کے اوقات",
            "قبلہ رخ", "ہجری کیلنڈر", "نور سے پوچھیں", "مینو", "نماز", "نور", "اطلاعات", "ترتیبات اور رازداری", "مدد اور رائے", "کمیونٹی معاہدہ",
            "سائن آؤٹ", "میرا پروفائل", "لوگ، پوسٹس، گروپس، #ہیش ٹیگ تلاش کریں", "تصویر/ویڈیو", "احساس", "زندگی کا واقعہ", "پوسٹ", "کچھ خوبصورت شیئر کریں…", "پسند", "تبصرہ",
            "شیئر", "محفوظ کریں", "تبصرہ لکھیں…", "بھیجیں", "ترجمہ دیکھیں", "اصل دیکھیں", "مجھے بتائیں کیا ہوا", "خلاصہ", "اسٹوری شامل کریں", "آج کے دن",
            "رجحانات", "جنہیں آپ جانتے ہوں", "ابھی آن لائن", "خوش آمدید", "حلال بُک جاری رکھنے کے لیے سائن ان کریں", "صارف نام یا ای میل", "پاس ورڈ", "پاس ورڈ بھول گئے؟", "سائن ان", "اکاؤنٹ بنائیں",
            "حلال بُک میں شامل ہوں", "آپ کا نام", "صارف نام", "ای میل", "جاری رکھیں", "زبان", "پوری امت تک پہنچائیں", "سے ترجمہ", "نگرانی", "ایڈمن کنسول",
        ],
        ["bn"] =
        [
            "হোম", "বন্ধুরা", "গ্রুপ", "পেজ", "ইভেন্ট", "রিলস", "লাইভ", "বার্তা", "সংরক্ষিত", "নামাজের সময়",
            "কিবলা নির্দেশক", "হিজরি ক্যালেন্ডার", "নূরকে জিজ্ঞাসা করুন", "মেনু", "নামাজ", "নূর", "বিজ্ঞপ্তি", "সেটিংস ও গোপনীয়তা", "সাহায্য ও মতামত", "কমিউনিটি চুক্তি",
            "সাইন আউট", "আমার প্রোফাইল", "মানুষ, পোস্ট, গ্রুপ, #হ্যাশট্যাগ খুঁজুন", "ছবি/ভিডিও", "অনুভূতি", "জীবনের ঘটনা", "পোস্ট", "সুন্দর কিছু শেয়ার করুন…", "লাইক", "মন্তব্য",
            "শেয়ার", "সংরক্ষণ", "একটি মন্তব্য লিখুন…", "পাঠান", "অনুবাদ দেখুন", "মূল দেখুন", "কী হলো জানুন", "সারসংক্ষেপ", "স্টোরি যোগ করুন", "এই দিনে",
            "ট্রেন্ডিং", "আপনি যাদের চিনতে পারেন", "এখন অনলাইনে", "আবার স্বাগতম", "হালালবুকে চালিয়ে যেতে সাইন ইন করুন", "ইউজারনেম বা ইমেইল", "পাসওয়ার্ড", "পাসওয়ার্ড ভুলে গেছেন?", "সাইন ইন", "অ্যাকাউন্ট খুলুন",
            "হালালবুকে যোগ দিন", "আপনার নাম", "ইউজারনেম", "ইমেইল", "চালিয়ে যান", "ভাষা", "পুরো উম্মাহর কাছে পৌঁছান", "থেকে অনূদিত", "মডারেশন", "অ্যাডমিন কনসোল",
        ],
        ["id"] =
        [
            "Beranda", "Teman", "Grup", "Halaman", "Acara", "Reels", "Live", "Pesan", "Tersimpan", "Waktu salat",
            "Arah kiblat", "Kalender Hijriah", "Tanya Noor", "Menu", "Salat", "Noor", "Notifikasi", "Pengaturan & privasi", "Bantuan & masukan", "Perjanjian Komunitas",
            "Keluar", "Profil saya", "Cari orang, postingan, grup, #tagar", "Foto/Video", "Perasaan", "Peristiwa hidup", "Posting", "Bagikan sesuatu yang indah…", "Suka", "Komentar",
            "Bagikan", "Simpan", "Tulis komentar…", "Kirim", "Lihat terjemahan", "Lihat asli", "Ringkasan untukku", "Ringkas", "Tambah cerita", "Hari ini di masa lalu",
            "Sedang tren", "Orang yang mungkin Anda kenal", "Sedang online", "Selamat datang kembali", "Masuk untuk melanjutkan ke HalalBook", "Nama pengguna atau email", "Kata sandi", "Lupa kata sandi?", "Masuk", "Buat akun",
            "Bergabung dengan HalalBook", "Nama Anda", "Nama pengguna", "Email", "Lanjutkan", "Bahasa", "Jangkau seluruh umat", "Diterjemahkan dari", "Moderasi", "Konsol admin",
        ],
        ["ms"] =
        [
            "Utama", "Rakan", "Kumpulan", "Halaman", "Acara", "Reels", "Siaran langsung", "Mesej", "Disimpan", "Waktu solat",
            "Arah kiblat", "Kalendar Hijrah", "Tanya Noor", "Menu", "Solat", "Noor", "Pemberitahuan", "Tetapan & privasi", "Bantuan & maklum balas", "Perjanjian Komuniti",
            "Log keluar", "Profil saya", "Cari orang, hantaran, kumpulan, #tanda pagar", "Foto/Video", "Perasaan", "Peristiwa hidup", "Hantar", "Kongsi sesuatu yang indah…", "Suka", "Komen",
            "Kongsi", "Simpan", "Tulis komen…", "Hantar", "Lihat terjemahan", "Lihat asal", "Kemas kini saya", "Ringkaskan", "Tambah cerita", "Pada hari ini",
            "Sohor kini", "Orang yang mungkin anda kenali", "Dalam talian", "Selamat kembali", "Log masuk untuk teruskan ke HalalBook", "Nama pengguna atau e-mel", "Kata laluan", "Lupa kata laluan?", "Log masuk", "Cipta akaun",
            "Sertai HalalBook", "Nama anda", "Nama pengguna", "E-mel", "Teruskan", "Bahasa", "Capai seluruh ummah", "Diterjemah daripada", "Moderasi", "Konsol pentadbir",
        ],
        ["tr"] =
        [
            "Ana sayfa", "Arkadaşlar", "Gruplar", "Sayfalar", "Etkinlikler", "Reels", "Canlı", "Mesajlar", "Kaydedilenler", "Namaz vakitleri",
            "Kıble bulucu", "Hicri takvim", "Nur'a sor", "Menü", "Namaz", "Nur", "Bildirimler", "Ayarlar ve gizlilik", "Yardım ve geri bildirim", "Topluluk Sözleşmesi",
            "Çıkış yap", "Profilim", "Kişi, gönderi, grup, #etiket ara", "Fotoğraf/Video", "Duygu", "Hayat olayı", "Paylaş", "Güzel bir şey paylaş…", "Beğen", "Yorum yap",
            "Paylaş", "Kaydet", "Yorum yaz…", "Gönder", "Çeviriyi gör", "Aslını gör", "Neler oldu?", "Özetle", "Hikâye ekle", "Bugün geçmişte",
            "Gündemde", "Tanıyor olabileceğin kişiler", "Şu an çevrimiçi", "Tekrar hoş geldin", "HalalBook'a devam etmek için giriş yap", "Kullanıcı adı veya e-posta", "Şifre", "Şifreni mi unuttun?", "Giriş yap", "Hesap oluştur",
            "HalalBook'a katıl", "Adın", "Kullanıcı adı", "E-posta", "Devam", "Dil", "Tüm ümmete ulaş", "Şu dilden çevrildi:", "Moderasyon", "Yönetim paneli",
        ],
        ["fr"] =
        [
            "Accueil", "Amis", "Groupes", "Pages", "Événements", "Reels", "En direct", "Messages", "Enregistrés", "Horaires de prière",
            "Direction de la Qibla", "Calendrier hégirien", "Demander à Noor", "Menu", "Prière", "Noor", "Notifications", "Paramètres et confidentialité", "Aide et avis", "Charte de la communauté",
            "Se déconnecter", "Mon profil", "Rechercher des personnes, publications, groupes, #hashtags", "Photo/Vidéo", "Humeur", "Événement de vie", "Publier", "Partagez quelque chose de beau…", "J'aime", "Commenter",
            "Partager", "Enregistrer", "Écrire un commentaire…", "Envoyer", "Voir la traduction", "Voir l'original", "Résumé pour moi", "Résumer", "Ajouter une story", "Ce jour-là",
            "Tendances", "Personnes que vous pourriez connaître", "En ligne", "Bon retour", "Connectez-vous pour continuer sur HalalBook", "Nom d'utilisateur ou e-mail", "Mot de passe", "Mot de passe oublié ?", "Se connecter", "Créer un compte",
            "Rejoindre HalalBook", "Votre nom", "Nom d'utilisateur", "E-mail", "Continuer", "Langue", "Toucher toute la Oumma", "Traduit de", "Modération", "Console d'administration",
        ],
    };

    public static IEnumerable<KeyValuePair<string, string>> For(string language) =>
        Values.TryGetValue(language, out var v) ? Keys.Zip(v, (k, t) => new KeyValuePair<string, string>(k, t)) : [];
}
