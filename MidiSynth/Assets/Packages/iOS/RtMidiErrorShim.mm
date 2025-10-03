// Assets/Plugins/iOS/RtMidiErrorShim.mm
// 4スペースインデント

// ---- 既存 libRtMidi.a 内の C++ 実体にリンクするための前方宣言 ----
class MidiApi;
enum RtMidiErrorType : int;
extern void rtmidi_error(MidiApi* api, RtMidiErrorType type, const char* msg);

// ---- C 記号のラッパーをエクスポート（Unity の DllImport が探す名前）----
extern "C" void rtmidi_error(void* api, int type, const char* msg)
{
    // C# からは void* と int で来るのでキャストして C++ 実体にフォワード
    rtmidi_error(reinterpret_cast<MidiApi*>(api),
                 static_cast<RtMidiErrorType>(type),
                 msg);
}

