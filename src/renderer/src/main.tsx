import React from 'react'
import ReactDOM from 'react-dom/client'
import { App } from './App'
import { browserMock } from './browserMock'
import './styles.css'
if (!window.desktop) window.desktop = browserMock()
ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><App /></React.StrictMode>)
