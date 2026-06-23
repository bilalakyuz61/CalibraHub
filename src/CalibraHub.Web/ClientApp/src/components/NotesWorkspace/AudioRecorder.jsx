import { useState, useRef, useEffect } from 'react'
import { X, Circle, Square, Pause, Play, Trash2, Upload } from 'lucide-react'
import './AudioRecorder.css'
import * as api from '../../services/notesService'

function fmt(s) {
  return String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0')
}

export function AudioRecorder({ open, onClose, noteId, onAttachmentAdded }) {
  var [phase, setPhase] = useState('idle')   // idle | recording | paused | done
  var [elapsed, setElapsed] = useState(0)
  var [audioUrl, setAudioUrl] = useState(null)
  var [uploading, setUploading] = useState(false)
  var [error, setError] = useState(null)

  var mediaRecRef  = useRef(null)
  var streamRef    = useRef(null)
  var chunksRef    = useRef([])
  var blobRef      = useRef(null)
  var mimeRef      = useRef('audio/webm')
  var timerRef     = useRef(null)
  var animRef      = useRef(null)
  var analyserRef  = useRef(null)
  var audioCtxRef  = useRef(null)
  var canvasRef    = useRef(null)

  useEffect(function () {
    if (!open) reset()
  }, [open])

  function reset() {
    if (streamRef.current) streamRef.current.getTracks().forEach(function (t) { t.stop() })
    if (timerRef.current) clearInterval(timerRef.current)
    if (animRef.current) cancelAnimationFrame(animRef.current)
    if (audioCtxRef.current) { try { audioCtxRef.current.close() } catch (_) {} }
    if (audioUrl) URL.revokeObjectURL(audioUrl)
    streamRef.current = null; mediaRecRef.current = null
    chunksRef.current = []; blobRef.current = null; analyserRef.current = null
    setPhase('idle'); setElapsed(0); setAudioUrl(null); setError(null)
  }

  async function startRecording() {
    setError(null)
    try {
      var stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      streamRef.current = stream

      var ctx = new AudioContext()
      audioCtxRef.current = ctx
      var src = ctx.createMediaStreamSource(stream)
      var analyser = ctx.createAnalyser()
      analyser.fftSize = 512
      src.connect(analyser)
      analyserRef.current = analyser

      var mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus' : 'audio/webm'
      mimeRef.current = mime
      var mr = new MediaRecorder(stream, { mimeType: mime })
      mediaRecRef.current = mr
      chunksRef.current = []

      mr.ondataavailable = function (e) { if (e.data.size > 0) chunksRef.current.push(e.data) }
      mr.onstop = function () {
        var blob = new Blob(chunksRef.current, { type: mimeRef.current })
        blobRef.current = blob
        setAudioUrl(URL.createObjectURL(blob))
        setPhase('done')
      }

      mr.start(100)
      setPhase('recording')
      timerRef.current = setInterval(function () { setElapsed(function (s) { return s + 1 }) }, 1000)
      drawWave()
    } catch (err) {
      setError('Mikrofon erişimi reddedildi: ' + err.message)
    }
  }

  function drawWave() {
    var canvas = canvasRef.current
    var analyser = analyserRef.current
    if (!canvas || !analyser) return
    var ctx = canvas.getContext('2d')
    var bufLen = analyser.frequencyBinCount
    var data = new Uint8Array(bufLen)

    function frame() {
      animRef.current = requestAnimationFrame(frame)
      analyser.getByteTimeDomainData(data)
      ctx.clearRect(0, 0, canvas.width, canvas.height)
      var waveColor = getComputedStyle(document.documentElement)
        .getPropertyValue('--ar-wave').trim() || '#6366f1'
      ctx.strokeStyle = waveColor; ctx.lineWidth = 2.5; ctx.beginPath()
      var sliceW = canvas.width / bufLen; var x = 0
      for (var i = 0; i < bufLen; i++) {
        var v = data[i] / 128; var y = (v * canvas.height) / 2
        i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y); x += sliceW
      }
      ctx.lineTo(canvas.width, canvas.height / 2); ctx.stroke()
    }
    frame()
  }

  function stopRecording() {
    if (mediaRecRef.current && mediaRecRef.current.state !== 'inactive') mediaRecRef.current.stop()
    if (streamRef.current) streamRef.current.getTracks().forEach(function (t) { t.stop() })
    clearInterval(timerRef.current)
    cancelAnimationFrame(animRef.current)
  }

  function pauseRecording() {
    if (mediaRecRef.current && mediaRecRef.current.state === 'recording') {
      mediaRecRef.current.pause()
      clearInterval(timerRef.current)
      cancelAnimationFrame(animRef.current)
      setPhase('paused')
    }
  }

  function resumeRecording() {
    if (mediaRecRef.current && mediaRecRef.current.state === 'paused') {
      mediaRecRef.current.resume()
      timerRef.current = setInterval(function () { setElapsed(function (s) { return s + 1 }) }, 1000)
      drawWave()
      setPhase('recording')
    }
  }

  function discardAndRetry() { reset() }

  function upload() {
    if (!blobRef.current || !noteId) return
    setUploading(true); setError(null)
    var d = new Date()
    var dateStr = d.getFullYear() + '-' + String(d.getMonth()+1).padStart(2,'0') + '-' + String(d.getDate()).padStart(2,'0')
      + ' ' + String(d.getHours()).padStart(2,'0') + '-' + String(d.getMinutes()).padStart(2,'0')
    var filename = 'Ses Kayit ' + dateStr + '.webm'
    var file = new File([blobRef.current], filename, { type: blobRef.current.type })
    api.uploadAttachment(noteId, file)
      .then(function (data) {
        if (data.success) { onAttachmentAdded && onAttachmentAdded(); onClose() }
        else setError(data.message || 'Yükleme başarısız.')
      })
      .catch(function (e) { setError('Yükleme hatası: ' + e.message) })
      .finally(function () { setUploading(false) })
  }

  if (!open) return null

  return (
    <div className="ar-backdrop" onClick={function (e) { if (e.target === e.currentTarget) onClose() }}>
      <div className="ar-panel">
        <div className="ar-header">
          <span className="ar-title">Ses Kaydı</span>
          <button className="ar-close" onClick={onClose}><X size={18} /></button>
        </div>

        <div className="ar-wave-wrap">
          <canvas ref={canvasRef} className="ar-wave" width={460} height={100} />
          {phase === 'idle' && (
            <div className="ar-wave-idle">
              <Circle size={28} style={{ color: 'var(--ar-rec)' }} />
              <span>Kaydı başlatmak için butona bas</span>
            </div>
          )}
          {phase === 'done' && (
            <div className="ar-wave-idle">
              <div className="ar-done-icon">✓</div>
              <span>Kayıt tamamlandı</span>
            </div>
          )}
        </div>

        <div className="ar-timer">{fmt(elapsed)}</div>

        {phase === 'done' && audioUrl && (
          <audio className="ar-preview" controls src={audioUrl} />
        )}

        {error && <div className="ar-error">{error}</div>}

        <div className="ar-controls">
          {phase === 'idle' && (
            <button className="ar-btn ar-btn--record" onClick={startRecording}>
              <Circle size={14} fill="currentColor" /> Kaydı Başlat
            </button>
          )}
          {phase === 'recording' && (<>
            <button className="ar-btn ar-btn--pause" onClick={pauseRecording}>
              <Pause size={14} /> Duraklat
            </button>
            <button className="ar-btn ar-btn--stop" onClick={stopRecording}>
              <Square size={14} fill="currentColor" /> Durdur
            </button>
          </>)}
          {phase === 'paused' && (<>
            <button className="ar-btn ar-btn--record" onClick={resumeRecording}>
              <Play size={14} /> Devam
            </button>
            <button className="ar-btn ar-btn--stop" onClick={stopRecording}>
              <Square size={14} fill="currentColor" /> Durdur
            </button>
          </>)}
          {phase === 'done' && (<>
            <button className="ar-btn ar-btn--reset" onClick={discardAndRetry}>
              <Trash2 size={14} /> Tekrar Kaydet
            </button>
            <button className="ar-btn ar-btn--upload" onClick={upload} disabled={uploading}>
              <Upload size={14} /> {uploading ? 'Yükleniyor…' : 'Notaya Ekle'}
            </button>
          </>)}
        </div>
      </div>
    </div>
  )
}
