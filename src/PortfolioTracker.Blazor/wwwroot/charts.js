let allocationChart = null;

// Reads the theme tokens defined in app.css so the chart follows the UI theme.
const ptColor = (name, fallback) => {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
};

// Draws the percentage of each slice directly on the doughnut.
// Slices under 4% stay clean (no label).
const percentOnSlice = {
    id: 'percentOnSlice',
    afterDraw(chart) {
        const meta = chart.getDatasetMeta(0);
        if (!meta || !meta.data) return;
        const values = chart.data.datasets[0].data.map(Number);
        const total = values.reduce((a, b) => a + b, 0);
        if (!total) return;

        const ctx = chart.ctx;
        ctx.save();
        ctx.font = 'bold 12px sans-serif';
        ctx.fillStyle = '#ffffff';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.shadowColor = 'rgba(0,0,0,0.55)';
        ctx.shadowBlur = 3;

        meta.data.forEach((arc, i) => {
            const pct = (values[i] / total) * 100;
            if (pct < 4) return;
            const m = arc.getProps(['x', 'y', 'startAngle', 'endAngle', 'innerRadius', 'outerRadius'], true);
            const mid = (m.startAngle + m.endAngle) / 2;
            const r = m.innerRadius + (m.outerRadius - m.innerRadius) * 0.62;
            ctx.fillText(pct.toFixed(1) + '%', m.x + Math.cos(mid) * r, m.y + Math.sin(mid) * r);
        });

        ctx.restore();
    }
};

window.updateAllocationChart = function (labels, data, colors, fullNames) {
    const ctx = document.getElementById('allocationChart');
    if (!ctx) return;

    if (allocationChart) {
        allocationChart.destroy();
    }

    allocationChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors,
                borderWidth: 2,
                borderColor: ptColor('--pt-surface', '#151e32')
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    position: 'right',
                    labels: {
                        padding: 12,
                        boxWidth: 14,
                        color: ptColor('--pt-text-muted', '#94a3b8'),
                        font: {
                            size: 11,
                            family: "Inter, system-ui, sans-serif"
                        }
                    }
                },
                tooltip: {
                    backgroundColor: ptColor('--pt-surface', '#151e32'),
                    titleColor: ptColor('--pt-text', '#f1f5f9'),
                    bodyColor: ptColor('--pt-text', '#f1f5f9'),
                    borderColor: ptColor('--pt-border', '#2a3b55'),
                    borderWidth: 1,
                    padding: 12,
                    callbacks: {
                        label: function (context) {
                            const name = (fullNames && fullNames[context.dataIndex]) || context.label || '';
                            return `${name}: ${context.parsed.toFixed(2)}%`;
                        }
                    }
                }
            }
        },
        plugins: [percentOnSlice]
    });
};
