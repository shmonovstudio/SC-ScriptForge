using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

public sealed partial class AiAssistantView
{
    private static Control ContextSidebar(Window owner,AiAssistantViewModel model,Button modelButton,TextBox query)
    {
        var root=Ui.Stack();root.Spacing=14;root.Children.Add(ReferenceUi.SmallTitle("Контекст и среда"));
        var environment=Ui.Stack(Ui.Row(Ui.IconBadge("computer",17),Ui.Text("Целевое окружение","section",12.5)),
            ReferenceUi.Metadata("computer","Компьютер",Environment.MachineName),ReferenceUi.Metadata("terminal","Профиль","PowerShell · Windows"),ReferenceUi.Metadata("shield","Права","Текущий пользователь"));environment.Spacing=17;
        var context=Ui.Bound(nameof(model.ContextStatus),"muted");context.FontSize=10.5;
        var contextButton=Ui.Button("Прикрепить скрипт",()=>AiContextWindow.ShowAsync(owner,model));contextButton.Content=Ui.Row(Ui.Icon("attach",14),Ui.Text("Выбрать скрипт",size:11.5));
        environment.Children.Add(Feedback.Chip("Локальное окружение",Tone.Success));root.Children.Add(Ui.Card(environment));root.Children.Add(ReferenceUi.SmallTitle("Полезные запросы"));
        foreach(var (icon,title,description,prompt) in new[]{
            ("terminal","Проверить статус служб","Показать остановленные службы","Как проверить остановленные службы Windows с помощью PowerShell?"),
            ("users","Работа с Active Directory","Пользователи, компьютеры, группы","Напиши скрипт, который найдёт компьютеры в Active Directory без входа более 90 дней"),
            ("computer","Диагностика системы","Диски, память, производительность","Напиши скрипт диагностики системы: память, диски и время работы"),
            ("shield","Безопасность","Аудит, права, политики","Как проверить локальные политики безопасности Windows через PowerShell?"),
            ("layers","Автоматизация","Планировщик, публикация, деплой","Объясни, как создать задачу в планировщике Windows для PowerShell-скрипта")})
        {
            var button=Ui.Action(title,()=>{model.Instruction=prompt;query.Focus();query.CaretIndex=prompt.Length;});button.Classes.Add("motionItem");button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new(10,12);
            var row=new Grid{ColumnDefinitions=new("38,*"),ColumnSpacing=12};var badge=Ui.IconBadge(icon,17);badge.Width=badge.Height=38;row.Children.Add(badge);
            var copy=Ui.Stack(Ui.Text(title,"section",12),Ui.Text(description,"muted",11.5));copy.Spacing=4;row.Children.Add(copy.Place(0,1));button.Content=row;root.Children.Add(button);
        }
        root.Children.Add(ReferenceUi.Line());root.Children.Add(ReferenceUi.SmallTitle("Модель помощника"));modelButton.HorizontalAlignment=HorizontalAlignment.Stretch;modelButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;root.Children.Add(modelButton);
        var tip=Ui.Card(Ui.Stack(Ui.Row(Ui.Icon("ai",18),Ui.Text("Совет","accent",12)),Ui.Text("Указывайте версию PowerShell и окружение, в котором будет выполняться скрипт — так ответы будут точнее.","muted",12)));Ui.Resource(tip,Border.BackgroundProperty,"ScSelected");root.Children.Add(tip);return root;
    }
}
