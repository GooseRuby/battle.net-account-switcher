using System.Globalization;
using System.Windows;

internal static class UiText
{
    public static bool English = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "ru";

    public static string T(string russian, string english) { return English ? english : russian; }

    public static void Apply(ResourceDictionary resources)
    {
        resources["WindowTitleText"] = "battle.net account switcher";
        resources["HeaderText"] = T("ВЫБОР АККАУНТА", "SELECT ACCOUNT");
        resources["SettingsText"] = T("Настройки", "Settings");
        resources["MinimizeText"] = T("Свернуть", "Minimize");
        resources["CloseText"] = T("Закрыть", "Close");
        resources["RefreshText"] = T("Обновить", "Refresh");
        resources["RefreshHintText"] = T("Перечитать аккаунты, сохранённые в Battle.net", "Reload the accounts saved in Battle.net");
        resources["RecentText"] = T("Последний", "Last used");
        resources["OpenClientText"] = T("Открыть Battle.net", "Open Battle.net");
        resources["HideText"] = T("Убрать из списка", "Hide from list");
        resources["RestoreText"] = T("Вернуть в список", "Restore to list");
        resources["RenameText"] = T("Переименовать", "Rename");
        resources["ProfileNameText"] = T("Имя профиля", "Profile name");
        resources["RenameHintText"] = T("Можно указать BattleTag или любое имя для карточки.", "Use your BattleTag or any name for this card.");
        resources["CancelText"] = T("Отмена", "Cancel");
        resources["SaveText"] = T("Сохранить", "Save");
        resources["UpdatesText"] = T("Обновления с GitHub", "GitHub updates");
        resources["CheckUpdatesText"] = T("Проверить обновления", "Check for updates");
        resources["OpenReleaseText"] = T("Открыть релиз", "Open release");
        resources["AboutText"] = T("О проекте", "About");
        resources["LegalText"] = T("Это неофициальный проект. Он не связан с Blizzard Entertainment и не одобрен ею. Battle.net и Blizzard Entertainment — товарные знаки Blizzard Entertainment, Inc. Названия используются для обозначения совместимости. Автор проекта не претендует на права на эти товарные знаки.", "This is an unofficial project. It is not affiliated with or endorsed by Blizzard Entertainment. Battle.net and Blizzard Entertainment are trademarks of Blizzard Entertainment, Inc. The names identify the compatible launcher. The project author claims no rights to these trademarks.");
        resources["DoneText"] = T("Готово", "Done");
        resources["GeneralText"] = T("Общие", "General");
        resources["LanguageText"] = T("ЯЗЫК ПРИЛОЖЕНИЯ", "APP LANGUAGE");
        resources["AutoUpdateText"] = T("Автопроверка обновлений при запуске", "Check for updates at startup");
    }
}
