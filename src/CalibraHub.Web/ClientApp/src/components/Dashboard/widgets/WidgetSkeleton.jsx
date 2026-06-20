/**
 * WidgetSkeleton — Widget veri yuklenirken gosterilen shimmer iskelet.
 * Dashboard.css .dash-skel pattern'ini kullanir.
 *
 * Props: { lines = 3 } — kac satir shimmer cizgisi.
 */
export default function WidgetSkeleton(props) {
  var lines = props.lines || 3
  var arr = []
  for (var i = 0; i < lines; i++) {
    arr.push(i)
  }
  return (
    <div aria-busy="true" aria-label="Yükleniyor">
      {arr.map(function (i) {
        return (
          <div
            key={i}
            className="dash-skel dash-skel-line"
            style={{ width: (i % 3 === 0 ? '90%' : i % 3 === 1 ? '70%' : '80%') }}
          />
        )
      })}
    </div>
  )
}
