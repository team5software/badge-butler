{{- define "badge-butler.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "badge-butler.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "badge-butler.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "badge-butler.labels" -}}
helm.sh/chart: {{ include "badge-butler.chart" . }}
{{ include "badge-butler.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{- define "badge-butler.selectorLabels" -}}
app.kubernetes.io/name: {{ include "badge-butler.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "badge-butler.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "badge-butler.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{- define "badge-butler.secretName" -}}
{{- if .Values.database.existingSecret.name -}}
{{- .Values.database.existingSecret.name -}}
{{- else -}}
{{- include "badge-butler.fullname" . -}}
{{- end -}}
{{- end -}}

{{- define "badge-butler.authSecretName" -}}
{{- if .Values.auth.existingSecret.name -}}
{{- .Values.auth.existingSecret.name -}}
{{- else -}}
{{- include "badge-butler.fullname" . -}}
{{- end -}}
{{- end -}}
