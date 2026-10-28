namespace Wavebreak.Meta;

public static partial class ArabicText
{
    internal static readonly (string En, string Ar)[] DualHooks =
    {
        ("Left hook (city)", "الخطّاف الأيسر (المدينة)"),
        ("Right hook (city)", "الخطّاف الأيمن (المدينة)"),
        ("Dash / core strike (city)", "اندفاع / ضربة النواة (المدينة)"),
        ("{0}: attached BOOST  |  {1}: dash / airborne core strike  |  Boost ready in {2:0.0}s", "{0}: اندفاع أثناء التثبيت  |  {1}: اندفاع / ضربة النواة في الهواء  |  جاهز بعد {2:0.0} ث"),
        ("Hold {0}: LEFT hook  |  Hold {1}: RIGHT hook", "اضغط مطولاً {0}: الخطّاف الأيسر  |  {1}: الخطّاف الأيمن"),
        ("Hold {0}: LEFT hook  |  Hold {1}: RIGHT hook  |  Release either independently", "اضغط مطولاً {0}: الخطّاف الأيسر  |  {1}: الخطّاف الأيمن  |  اترك كل خطّاف بشكل مستقل"),
        ("{0}: attached BOOST  |  {1}: core strike  |  Boost ready in {2:0.0}s", "{0}: اندفاع مع بقاء التثبيت  |  {1}: ضربة النواة  |  الاندفاع جاهز بعد {2:0.0} ث"),
        ("Left: {0}  |  Right: {1}", "الأيسر: {0}  |  الأيمن: {1}"),
        ("Attached", "مثبّت"), ("Free", "حرّ"),
        ("{0} / {1}: independent hooks  |  {2}: attached boost", "{0} / {1}: خطّافان مستقلان  |  {2}: اندفاع أثناء التثبيت"),
        ("Select a map to preview its hero; you can choose another afterward", "اختر خريطة لمعاينة شخصيتها؛ يمكنك اختيار شخصية أخرى بعدها"),
        ("Character: {0}  ·  {1}", "الشخصية: {0}  ·  {1}"),
        ("North Bellspire", "أبراج الأجراس الشمالية"), ("Foundry Quarter", "حي المسبك"),
        ("Westgate Heights", "مرتفعات البوابة الغربية"), ("Southward Terraces", "المصاطب الجنوبية"),
    };
}
